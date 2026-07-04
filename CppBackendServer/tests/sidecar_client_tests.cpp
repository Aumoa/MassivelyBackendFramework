#include "cpp_backend/packet_core.hpp"

#include <array>
#include <exception>
#include <future>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#include <cerrno>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
#endif

using namespace cpp_backend;

namespace
{

#ifdef _WIN32
    using socket_handle = SOCKET;
    constexpr socket_handle invalid_socket_handle = INVALID_SOCKET;
#else
    using socket_handle = int;
    constexpr socket_handle invalid_socket_handle = -1;
#endif

    class socket_runtime
    {
    public:
        socket_runtime()
        {
#ifdef _WIN32
            WSADATA data{};
            if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
            {
                throw std::runtime_error("WSAStartup failed.");
            }
#endif
        }

        ~socket_runtime()
        {
#ifdef _WIN32
            WSACleanup();
#endif
        }
    };

    class socket_owner
    {
    public:
        socket_owner() = default;

        explicit socket_owner(socket_handle handle) : m_handle(handle) {}

        socket_owner(const socket_owner&) = delete;
        socket_owner& operator=(const socket_owner&) = delete;

        socket_owner(socket_owner&& other) noexcept : m_handle(other.m_handle)
        {
            other.m_handle = invalid_socket_handle;
        }

        socket_owner& operator=(socket_owner&& other) noexcept
        {
            if (this != &other)
            {
                reset();
                m_handle = other.m_handle;
                other.m_handle = invalid_socket_handle;
            }

            return *this;
        }

        ~socket_owner()
        {
            reset();
        }

        socket_handle get() const noexcept
        {
            return m_handle;
        }

        explicit operator bool() const noexcept
        {
            return m_handle != invalid_socket_handle;
        }

        void reset(socket_handle handle = invalid_socket_handle) noexcept
        {
            if (m_handle != invalid_socket_handle)
            {
#ifdef _WIN32
                closesocket(m_handle);
#else
                close(m_handle);
#endif
            }

            m_handle = handle;
        }

    private:
        socket_handle m_handle = invalid_socket_handle;
    };

    void require(bool condition, const char* message)
    {
        if (!condition)
        {
            throw std::runtime_error(message);
        }
    }

    void send_all(socket_handle socket, std::span<const std::uint8_t> bytes)
    {
        std::size_t sent = 0;
        while (sent < bytes.size())
        {
            auto remaining = bytes.size() - sent;
            auto chunk_size = remaining > static_cast<std::size_t>(std::numeric_limits<int>::max())
                                  ? std::numeric_limits<int>::max()
                                  : static_cast<int>(remaining);
#ifdef _WIN32
            int result = ::send(socket, reinterpret_cast<const char*>(bytes.data() + sent), chunk_size, 0);
#else
            int result = static_cast<int>(::send(socket, bytes.data() + sent, static_cast<std::size_t>(chunk_size), 0));
#endif
            if (result <= 0)
            {
                throw std::runtime_error("Socket send failed.");
            }

            sent += static_cast<std::size_t>(result);
        }
    }

    void receive_exact(socket_handle socket, std::span<std::uint8_t> bytes)
    {
        std::size_t received = 0;
        while (received < bytes.size())
        {
            auto remaining = bytes.size() - received;
            auto chunk_size = remaining > static_cast<std::size_t>(std::numeric_limits<int>::max())
                                  ? std::numeric_limits<int>::max()
                                  : static_cast<int>(remaining);
#ifdef _WIN32
            int result = ::recv(socket, reinterpret_cast<char*>(bytes.data() + received), chunk_size, 0);
#else
            int result =
                static_cast<int>(::recv(socket, bytes.data() + received, static_cast<std::size_t>(chunk_size), 0));
#endif
            if (result <= 0)
            {
                throw std::runtime_error("Socket receive failed.");
            }

            received += static_cast<std::size_t>(result);
        }
    }

    packet_frame read_frame(socket_handle socket)
    {
        std::array<std::uint8_t, packet_header::size> header_bytes{};
        receive_exact(socket, header_bytes);
        auto header = decode_header(header_bytes, sidecar_control_max_payload_length);
        std::vector<std::uint8_t> payload(header.payload_length);
        if (!payload.empty())
        {
            receive_exact(socket, payload);
        }

        return packet_frame{header, std::move(payload)};
    }

    void write_frame(socket_handle socket, const packet_frame& frame)
    {
        auto bytes = encode_frame(frame);
        send_all(socket, bytes);
    }

    socket_owner create_listener(std::uint16_t& port)
    {
        socket_owner listener(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
        if (!listener)
        {
            throw std::runtime_error("Could not create listener socket.");
        }

        int enabled = 1;
        setsockopt(listener.get(), SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&enabled), sizeof(enabled));

        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_port = 0;
#ifdef _WIN32
        if (InetPtonA(AF_INET, "127.0.0.1", &address.sin_addr) != 1)
        {
            throw std::runtime_error("Could not parse loopback address.");
        }
#else
        if (inet_pton(AF_INET, "127.0.0.1", &address.sin_addr) != 1)
        {
            throw std::runtime_error("Could not parse loopback address.");
        }
#endif

        if (::bind(listener.get(), reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0)
        {
            throw std::runtime_error("Could not bind listener socket.");
        }

        if (::listen(listener.get(), 1) != 0)
        {
            throw std::runtime_error("Could not listen on socket.");
        }

        sockaddr_in bound{};
        socklen_t bound_length = sizeof(bound);
        if (getsockname(listener.get(), reinterpret_cast<sockaddr*>(&bound), &bound_length) != 0)
        {
            throw std::runtime_error("Could not read listener port.");
        }

        port = ntohs(bound.sin_port);
        return listener;
    }

    socket_owner connect_loopback(std::uint16_t port)
    {
        socket_owner client(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
        if (!client)
        {
            throw std::runtime_error("Could not create client socket.");
        }

        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_port = htons(port);
#ifdef _WIN32
        if (InetPtonA(AF_INET, "127.0.0.1", &address.sin_addr) != 1)
        {
            throw std::runtime_error("Could not parse loopback address.");
        }
#else
        if (inet_pton(AF_INET, "127.0.0.1", &address.sin_addr) != 1)
        {
            throw std::runtime_error("Could not parse loopback address.");
        }
#endif

        if (::connect(client.get(), reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0)
        {
            throw std::runtime_error("Could not connect client socket.");
        }

        return client;
    }

    struct observed_validation_request
    {
        std::string code;
        std::string gateway_node_id;
        std::string gateway_master_connection_id;
    };

    struct observed_control_update
    {
        std::uint16_t packet_id = 0;
        bool flag = false;
        std::string detail;
    };

    std::future<observed_validation_request> accept_one_validation_request(socket_handle listener,
                                                                           bool wrong_request_id)
    {
        return std::async(
            std::launch::async,
            [listener, wrong_request_id]
            {
                socket_owner client(::accept(listener, nullptr, nullptr));
                if (!client)
                {
                    throw std::runtime_error("Fake sidecar accept failed.");
                }

                auto request_frame = read_frame(client.get());
                require(request_frame.header.kind == packet_kind::control, "Request frame kind mismatch.");
                require(request_frame.header.packet_id == sidecar_pid_direct_connect_validation_request,
                        "Request frame packet id mismatch.");
                require(request_frame.header.version == sidecar_control_schema_version,
                        "Request frame version mismatch.");
                auto request = decode_direct_connect_code_validation_request(request_frame.payload);

                auto response_request_id = request.request_id;
                if (wrong_request_id)
                {
                    response_request_id.fill(0);
                    response_request_id.back() = 1;
                }

                direct_connect_code_validation_response response{
                    response_request_id,       true,
                    request.gateway_node_id,   request.gateway_master_connection_id,
                    master_node_kind::backend, "cpp-backend",
                    "backend-master-a",        "",
                };

                auto response_frame = make_frame(packet_kind::control, sidecar_pid_direct_connect_validation_response,
                                                 sidecar_control_schema_version,
                                                 encode_direct_connect_code_validation_response(response));
                write_frame(client.get(), response_frame);

                return observed_validation_request{
                    request.code,
                    request.gateway_node_id,
                    request.gateway_master_connection_id,
                };
            });
    }

    std::future<std::vector<observed_control_update>> accept_control_updates(socket_handle listener, int count)
    {
        return std::async(
            std::launch::async,
            [listener, count]
            {
                std::vector<observed_control_update> updates;
                updates.reserve(static_cast<std::size_t>(count));

                for (int index = 0; index < count; ++index)
                {
                    socket_owner client(::accept(listener, nullptr, nullptr));
                    if (!client)
                    {
                        throw std::runtime_error("Fake sidecar control accept failed.");
                    }

                    auto request_frame = read_frame(client.get());
                    require(request_frame.header.kind == packet_kind::control, "Control request frame kind mismatch.");
                    require(request_frame.header.version == sidecar_control_schema_version,
                            "Control request frame version mismatch.");

                    sidecar_control_ack ack;
                    std::uint16_t ack_packet_id = 0;
                    if (request_frame.header.packet_id == sidecar_pid_endpoint_state_update)
                    {
                        auto update = decode_sidecar_endpoint_state_update(request_frame.payload);
                        updates.push_back(observed_control_update{
                            request_frame.header.packet_id,
                            update.ready,
                            update.detail,
                        });
                        ack.request_id = update.request_id;
                        ack.success = true;
                        ack_packet_id = sidecar_pid_endpoint_state_ack;
                    }
                    else if (request_frame.header.packet_id == sidecar_pid_shutdown_state_update)
                    {
                        auto update = decode_sidecar_shutdown_state_update(request_frame.payload);
                        updates.push_back(observed_control_update{
                            request_frame.header.packet_id,
                            update.shutting_down,
                            update.reason,
                        });
                        ack.request_id = update.request_id;
                        ack.success = true;
                        ack_packet_id = sidecar_pid_shutdown_state_ack;
                    }
                    else
                    {
                        throw std::runtime_error("Unexpected sidecar control packet id.");
                    }

                    auto ack_frame = make_frame(packet_kind::control, ack_packet_id, sidecar_control_schema_version,
                                                encode_sidecar_control_ack(ack));
                    write_frame(client.get(), ack_frame);
                }

                return updates;
            });
    }

    class recording_validator final : public direct_connect_code_validator
    {
    public:
        int call_count = 0;
        std::string code;
        std::string gateway_node_id;
        std::string gateway_master_connection_id;

        direct_connect_code_validation_response validate(
            const std::string& requested_code, const std::string& requested_gateway_node_id,
            const std::string& requested_gateway_master_connection_id) override
        {
            ++call_count;
            code = requested_code;
            gateway_node_id = requested_gateway_node_id;
            gateway_master_connection_id = requested_gateway_master_connection_id;
            return direct_connect_code_validation_response{
                {},
                true,
                requested_gateway_node_id,
                requested_gateway_master_connection_id,
                master_node_kind::backend,
                "cpp-backend",
                "backend-master-a",
                "",
            };
        }
    };

    void sidecar_validator_sends_validation_request()
    {
        socket_runtime runtime;
        std::uint16_t port = 0;
        auto listener = create_listener(port);
        auto server = accept_one_validation_request(listener.get(), false);

        sidecar_direct_connect_code_validator validator({"127.0.0.1", port});
        auto response = validator.validate("code-1", "gateway-a", "gateway-master-a");
        auto observed = server.get();

        require(response.success, "Expected sidecar validation success.");
        require(response.gateway_node_id == "gateway-a", "Response gateway node mismatch.");
        require(response.gateway_master_connection_id == "gateway-master-a",
                "Response gateway Master connection mismatch.");
        require(response.target_node_kind == master_node_kind::backend, "Response target kind mismatch.");
        require(observed.code == "code-1", "Observed request code mismatch.");
        require(observed.gateway_node_id == "gateway-a", "Observed request gateway node mismatch.");
        require(observed.gateway_master_connection_id == "gateway-master-a",
                "Observed request gateway Master connection mismatch.");
    }

    void sidecar_validator_rejects_mismatched_response_id()
    {
        socket_runtime runtime;
        std::uint16_t port = 0;
        auto listener = create_listener(port);
        auto server = accept_one_validation_request(listener.get(), true);

        sidecar_direct_connect_code_validator validator({"127.0.0.1", port});
        try
        {
            (void)validator.validate("code-1", "gateway-a", "gateway-master-a");
        }
        catch (const packet_error&)
        {
            (void)server.get();
            return;
        }

        throw std::runtime_error("Mismatched sidecar response id did not throw.");
    }

    void sidecar_control_client_reports_shutdown_and_reconnects_endpoint_state()
    {
        socket_runtime runtime;
        std::uint16_t port = 0;
        auto listener = create_listener(port);
        auto server = accept_control_updates(listener.get(), 4);

        sidecar_control_client client({"127.0.0.1", port});
        auto ready_ack = client.update_endpoint_state(true, "listener ready");
        require(ready_ack.success, "Expected endpoint ready ack success.");
        auto shutdown_ack = client.update_shutdown_state(true, "rolling restart");
        require(shutdown_ack.success, "Expected shutdown ack success.");
        auto clear_ack = client.update_shutdown_state(false, "restart complete");
        require(clear_ack.success, "Expected shutdown clear ack success.");
        auto reconnected_ack = client.update_endpoint_state(true, "listener reconnected");
        require(reconnected_ack.success, "Expected reconnected endpoint ack success.");

        auto updates = server.get();
        require(updates.size() == 4, "Expected four sidecar control updates.");
        require(updates[0].packet_id == sidecar_pid_endpoint_state_update, "First update packet mismatch.");
        require(updates[0].flag, "First endpoint update should be ready.");
        require(updates[0].detail == "listener ready", "First endpoint detail mismatch.");
        require(updates[1].packet_id == sidecar_pid_shutdown_state_update, "Second update packet mismatch.");
        require(updates[1].flag, "Shutdown update should request shutdown.");
        require(updates[1].detail == "rolling restart", "Shutdown reason mismatch.");
        require(updates[2].packet_id == sidecar_pid_shutdown_state_update, "Third update packet mismatch.");
        require(!updates[2].flag, "Shutdown clear update should clear shutdown.");
        require(updates[2].detail == "restart complete", "Shutdown clear reason mismatch.");
        require(updates[3].packet_id == sidecar_pid_endpoint_state_update, "Fourth update packet mismatch.");
        require(updates[3].flag, "Reconnected endpoint update should be ready.");
        require(updates[3].detail == "listener reconnected", "Reconnected endpoint detail mismatch.");
    }

    void gateway_direct_listener_accepts_gateway_handshake()
    {
        socket_runtime runtime;
        node_auth_challenge challenge;
        challenge.challenge_id = "challenge-a";
        for (std::size_t index = 0; index < challenge.nonce.size(); ++index)
        {
            challenge.nonce[index] = static_cast<std::uint8_t>(index);
        }

        recording_validator validator;
        gateway_direct_listener listener(
            gateway_listener_endpoint{
                "127.0.0.1",
                0,
                1,
            },
            challenge, validator);

        auto server =
            std::async(std::launch::async, [&listener] { return listener.accept_one("backend-connection-a"); });

        auto client = connect_loopback(listener.port());
        auto challenge_frame = read_frame(client.get());
        require(challenge_frame.header.kind == packet_kind::control, "Challenge frame kind mismatch.");
        require(challenge_frame.header.packet_id == master_pid_node_auth_challenge, "Challenge packet id mismatch.");
        auto decoded_challenge = decode_node_auth_challenge(challenge_frame.payload);
        require(decoded_challenge.challenge_id == challenge.challenge_id, "Challenge id mismatch.");
        require(decoded_challenge.nonce == challenge.nonce, "Challenge nonce mismatch.");

        auto hello_frame = make_frame(packet_kind::control, master_pid_node_hello, master_control_schema_version,
                                      encode_node_hello(node_hello{
                                          master_node_kind::gateway,
                                          master_control_schema_version,
                                          "gateway-a",
                                          "Gateway A",
                                          "gateway-master-a",
                                      }));
        write_frame(client.get(), hello_frame);

        auto code_frame =
            make_frame(packet_kind::control, master_pid_direct_connect_code, master_control_schema_version,
                       encode_direct_connect_code(direct_connect_code{
                           "code-1",
                       }));
        write_frame(client.get(), code_frame);

        auto accepted_frame = read_frame(client.get());
        require(accepted_frame.header.kind == packet_kind::control, "Accepted frame kind mismatch.");
        require(accepted_frame.header.packet_id == master_pid_node_accepted, "Accepted packet id mismatch.");
        auto accepted = decode_node_accepted(accepted_frame.payload);
        require(accepted.node_id == "gateway-a", "Accepted node id mismatch.");
        require(accepted.connection_id == "backend-connection-a", "Accepted connection id mismatch.");

        auto result = server.get();
        require(result.gateway_node_id == "gateway-a", "Server result gateway node mismatch.");
        require(result.gateway_master_connection_id == "gateway-master-a", "Server result Gateway Master id mismatch.");
        require(validator.call_count == 1, "Validator call count mismatch.");
        require(validator.code == "code-1", "Validator code mismatch.");
    }

} // namespace

int main()
{
    try
    {
        sidecar_validator_sends_validation_request();
        sidecar_validator_rejects_mismatched_response_id();
        sidecar_control_client_reports_shutdown_and_reconnects_endpoint_state();
        gateway_direct_listener_accepts_gateway_handshake();
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }

    return 0;
}
