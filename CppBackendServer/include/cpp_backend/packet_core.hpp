#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <span>
#include <stdexcept>
#include <string>
#include <unordered_set>
#include <vector>

namespace cpp_backend
{

    class packet_error : public std::runtime_error
    {
    public:
        explicit packet_error(const std::string& message);
    };

    enum class packet_kind : std::uint8_t
    {
        request = 0,
        response = 1,
        notify = 2,
        control = 3,
    };

    using guid_bytes = std::array<std::uint8_t, 16>;

    struct packet_header
    {
        static constexpr std::size_t size = 8;
        static constexpr std::uint32_t max_payload_length = 0xFFFFFF;

        packet_kind kind = packet_kind::control;
        std::uint8_t flags = 0;
        std::uint16_t packet_id = 0;
        std::uint16_t version = 0;
        std::uint32_t payload_length = 0;
    };

    struct packet_frame
    {
        packet_header header;
        std::vector<std::uint8_t> payload;
    };

    class packet_writer
    {
    public:
        void write_byte(std::uint8_t value);
        void write_uint16(std::uint16_t value);
        void write_uint32(std::uint32_t value);
        void write_int32(std::int32_t value);
        void write_int64(std::int64_t value);
        void write_guid(const guid_bytes& value);
        void write_string(const std::string& value);
        void write_bytes(std::span<const std::uint8_t> value);

        const std::vector<std::uint8_t>& bytes() const noexcept;
        std::vector<std::uint8_t> take_bytes();

    private:
        std::vector<std::uint8_t> m_bytes;
    };

    class packet_reader
    {
    public:
        explicit packet_reader(std::span<const std::uint8_t> bytes);

        std::uint8_t read_byte();
        std::uint16_t read_uint16();
        std::uint32_t read_uint32();
        std::int32_t read_int32();
        std::int64_t read_int64();
        guid_bytes read_guid();
        std::string read_string();
        std::vector<std::uint8_t> read_bytes(std::size_t length);
        void require_finished() const;

        std::size_t remaining() const noexcept;

    private:
        void ensure_available(std::size_t count) const;

        std::span<const std::uint8_t> m_bytes;
        std::size_t m_position = 0;
    };

    std::array<std::uint8_t, packet_header::size> encode_header(const packet_header& header);
    packet_header decode_header(std::span<const std::uint8_t> bytes, std::uint32_t max_payload_length);
    std::vector<std::uint8_t> encode_frame(const packet_frame& frame);
    packet_frame make_frame(packet_kind kind, std::uint16_t packet_id, std::uint16_t version,
                            std::vector<std::uint8_t> payload, std::uint8_t flags = 0);

    constexpr std::uint16_t master_control_schema_version = 8;
    constexpr std::uint16_t master_pid_node_auth_challenge = 100;
    constexpr std::uint16_t master_pid_node_hello = 101;
    constexpr std::uint16_t master_pid_node_accepted = 103;
    constexpr std::uint16_t master_pid_direct_connect_code = 115;
    constexpr std::size_t master_auth_nonce_length = 32;
    constexpr std::uint32_t master_max_handshake_payload_length = 16 * 1024;

    enum class master_node_kind : std::uint8_t
    {
        unknown = 0,
        gateway = 1,
        dedicated = 2,
        master_admin = 3,
        backend = 4,
    };

    constexpr std::uint16_t gateway_backend_channel_version = 1;
    constexpr std::uint16_t pid_gate_backend_channel_data = 8;
    constexpr std::uint16_t pid_gate_backend_channel_close = 9;
    constexpr std::uint16_t pid_gate_backend_channel_open = 10;

    constexpr std::uint16_t sidecar_control_schema_version = 1;
    constexpr std::uint32_t sidecar_control_max_payload_length = 1024 * 1024;
    constexpr std::uint16_t sidecar_pid_direct_connect_validation_request = 1;
    constexpr std::uint16_t sidecar_pid_direct_connect_validation_response = 2;
    constexpr std::uint16_t sidecar_pid_endpoint_state_update = 3;
    constexpr std::uint16_t sidecar_pid_endpoint_state_ack = 4;
    constexpr std::uint16_t sidecar_pid_runtime_status_update = 5;
    constexpr std::uint16_t sidecar_pid_runtime_status_ack = 6;
    constexpr std::uint16_t sidecar_pid_shutdown_state_update = 7;
    constexpr std::uint16_t sidecar_pid_shutdown_state_ack = 8;
    constexpr std::uint16_t sidecar_pid_manifest_declaration_update = 9;
    constexpr std::uint16_t sidecar_pid_manifest_declaration_ack = 10;
    constexpr std::uint16_t sidecar_pid_manifest_snapshot_request = 11;
    constexpr std::uint16_t sidecar_pid_manifest_snapshot_response = 12;

    constexpr std::int32_t backend_packet_manifest_max_manifest_count = 2048;
    constexpr std::int32_t backend_packet_manifest_max_entry_count = 4096;

    enum class backend_packet_manifest_direction : std::uint8_t
    {
        client_to_backend = 1,
        backend_to_client = 2,
    };

    enum class backend_packet_manifest_entry_status : std::uint8_t
    {
        active = 1,
        deprecated = 2,
    };

    struct gateway_backend_channel_open
    {
        std::uint32_t channel_id = 0;
        std::optional<std::string> principal_subject_id;
    };

    struct gateway_backend_channel_data_envelope
    {
        std::uint32_t channel_id = 0;
        packet_kind routed_kind = packet_kind::request;
        std::uint16_t routed_packet_id = 0;
        std::uint16_t routed_version = 0;
        std::optional<guid_bytes> exchange_id;
        std::vector<std::uint8_t> routed_payload;
    };

    struct gateway_backend_channel_close
    {
        std::uint32_t channel_id = 0;
        std::string reason;
    };

    struct direct_connect_code_validation_request
    {
        guid_bytes request_id{};
        std::string code;
        std::string gateway_node_id;
        std::string gateway_master_connection_id;
    };

    struct sidecar_manifest_snapshot_request
    {
        guid_bytes request_id{};
    };

    struct sidecar_endpoint_state_update
    {
        guid_bytes request_id{};
        bool ready = false;
        std::string detail;
    };

    struct sidecar_runtime_status_update
    {
        guid_bytes request_id{};
        bool healthy = false;
        std::int32_t active_gateway_sessions = 0;
        std::int32_t active_channels = 0;
        std::string detail;
    };

    struct sidecar_shutdown_state_update
    {
        guid_bytes request_id{};
        bool shutting_down = false;
        std::string reason;
    };

    struct sidecar_manifest_declaration_update
    {
        guid_bytes request_id{};
        std::string manifest_id;
        std::string manifest_hash;
    };

    struct sidecar_control_ack
    {
        guid_bytes request_id{};
        bool success = false;
        std::string error_message;
    };

    struct backend_packet_payload_constraint
    {
        std::int32_t minimum_length = 0;
        std::int32_t maximum_length = 0;
        std::optional<std::int32_t> fixed_length;
        std::string schema_id;
        std::optional<std::string> schema_hash;
    };

    struct backend_packet_manifest_entry
    {
        backend_packet_manifest_direction direction = backend_packet_manifest_direction::client_to_backend;
        packet_kind kind = packet_kind::request;
        std::uint16_t packet_id = 0;
        std::uint16_t routed_version = 0;
        backend_packet_payload_constraint payload_constraint;
        backend_packet_manifest_entry_status status = backend_packet_manifest_entry_status::active;
    };

    struct backend_packet_manifest
    {
        std::string backend_kind;
        std::string manifest_id;
        std::string hash;
        std::vector<backend_packet_manifest_entry> entries;
    };

    struct backend_packet_manifest_snapshot
    {
        std::vector<backend_packet_manifest> manifests;
        std::int64_t observed_at_unix_milliseconds = 0;
    };

    struct sidecar_manifest_snapshot_response
    {
        guid_bytes request_id{};
        bool success = false;
        std::optional<backend_packet_manifest_snapshot> snapshot;
        std::string error_message;
    };

    struct node_auth_challenge
    {
        std::string challenge_id;
        std::array<std::uint8_t, master_auth_nonce_length> nonce{};
    };

    struct node_hello
    {
        master_node_kind node_kind = master_node_kind::unknown;
        std::uint16_t protocol_version = 0;
        std::string node_id;
        std::string display_name;
        std::string master_connection_id;
    };

    struct direct_connect_code
    {
        std::string code;
    };

    struct node_accepted
    {
        std::string node_id;
        std::string connection_id;
    };

    struct direct_connect_code_validation_response
    {
        guid_bytes request_id{};
        bool success = false;
        std::string gateway_node_id;
        std::string gateway_master_connection_id;
        master_node_kind target_node_kind = master_node_kind::unknown;
        std::string target_node_id;
        std::string target_master_connection_id;
        std::string error_message;
    };

    class direct_connect_code_validator
    {
    public:
        virtual ~direct_connect_code_validator() = default;

        virtual direct_connect_code_validation_response validate(const std::string& code,
                                                                 const std::string& gateway_node_id,
                                                                 const std::string& gateway_master_connection_id) = 0;
    };

    struct sidecar_control_endpoint
    {
        std::string host = "127.0.0.1";
        std::uint16_t port = 0;
    };

    class sidecar_direct_connect_code_validator final : public direct_connect_code_validator
    {
    public:
        explicit sidecar_direct_connect_code_validator(sidecar_control_endpoint endpoint);

        direct_connect_code_validation_response validate(const std::string& code, const std::string& gateway_node_id,
                                                         const std::string& gateway_master_connection_id) override;

    private:
        sidecar_control_endpoint m_endpoint;
    };

    class sidecar_control_client final
    {
    public:
        explicit sidecar_control_client(sidecar_control_endpoint endpoint);

        sidecar_control_ack update_endpoint_state(bool ready, std::string detail);

        sidecar_control_ack update_shutdown_state(bool shutting_down, std::string reason);

    private:
        sidecar_control_endpoint m_endpoint;
    };

    struct gateway_direct_handshake_result
    {
        std::string gateway_node_id;
        std::string gateway_master_connection_id;
        packet_frame accepted_frame;
    };

    struct gateway_listener_endpoint
    {
        std::string host = "127.0.0.1";
        std::uint16_t port = 0;
        int backlog = 16;
    };

    class gateway_direct_listener
    {
    public:
        gateway_direct_listener(gateway_listener_endpoint endpoint, node_auth_challenge challenge,
                                direct_connect_code_validator& validator);
        ~gateway_direct_listener();

        gateway_direct_listener(const gateway_direct_listener&) = delete;
        gateway_direct_listener& operator=(const gateway_direct_listener&) = delete;
        gateway_direct_listener(gateway_direct_listener&&) noexcept;
        gateway_direct_listener& operator=(gateway_direct_listener&&) noexcept;

        std::uint16_t port() const noexcept;

        gateway_direct_handshake_result accept_one(const std::string& backend_connection_id);

    private:
        class impl;
        std::unique_ptr<impl> m_impl;
    };

    enum class gateway_session_event_kind
    {
        ignored,
        channel_open,
        channel_data,
        channel_close,
    };

    struct gateway_session_event
    {
        gateway_session_event_kind kind = gateway_session_event_kind::ignored;
        std::optional<gateway_backend_channel_open> open;
        std::optional<gateway_backend_channel_data_envelope> data;
        std::optional<gateway_backend_channel_close> close;
    };

    class gateway_frame_writer
    {
    public:
        virtual ~gateway_frame_writer() = default;

        virtual void write(packet_frame frame) = 0;
    };

    class trusted_gateway_session
    {
    public:
        trusted_gateway_session(std::string gateway_node_id, std::string gateway_master_connection_id);

        gateway_session_event handle_gateway_frame(const packet_frame& frame);

        void write_channel_data(gateway_frame_writer& writer, std::uint32_t channel_id, packet_kind routed_kind,
                                std::uint16_t routed_packet_id, std::uint16_t routed_version,
                                std::optional<guid_bytes> exchange_id, std::vector<std::uint8_t> routed_payload);

        void write_channel_close(gateway_frame_writer& writer, std::uint32_t channel_id, const std::string& reason);

        void mark_disconnected();

        bool has_channel(std::uint32_t channel_id) const;
        std::size_t channel_count() const;

        const std::string& gateway_node_id() const noexcept;
        const std::string& gateway_master_connection_id() const noexcept;

    private:
        void require_channel_open(std::uint32_t channel_id) const;

        std::string m_gateway_node_id;
        std::string m_gateway_master_connection_id;
        mutable std::mutex m_sync;
        std::mutex m_write_sync;
        std::unordered_set<std::uint32_t> m_channels;
    };

    std::vector<std::uint8_t> encode_gateway_backend_channel_open(const gateway_backend_channel_open& value);
    gateway_backend_channel_open decode_gateway_backend_channel_open(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_gateway_backend_channel_data_envelope(
        const gateway_backend_channel_data_envelope& value);
    gateway_backend_channel_data_envelope decode_gateway_backend_channel_data_envelope(
        std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_gateway_backend_channel_close(const gateway_backend_channel_close& value);
    gateway_backend_channel_close decode_gateway_backend_channel_close(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_direct_connect_code_validation_request(
        const direct_connect_code_validation_request& value);
    direct_connect_code_validation_request decode_direct_connect_code_validation_request(
        std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_manifest_snapshot_request(const sidecar_manifest_snapshot_request& value);
    sidecar_manifest_snapshot_request decode_sidecar_manifest_snapshot_request(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_endpoint_state_update(const sidecar_endpoint_state_update& value);
    sidecar_endpoint_state_update decode_sidecar_endpoint_state_update(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_runtime_status_update(const sidecar_runtime_status_update& value);
    sidecar_runtime_status_update decode_sidecar_runtime_status_update(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_shutdown_state_update(const sidecar_shutdown_state_update& value);
    sidecar_shutdown_state_update decode_sidecar_shutdown_state_update(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_manifest_declaration_update(
        const sidecar_manifest_declaration_update& value);
    sidecar_manifest_declaration_update decode_sidecar_manifest_declaration_update(
        std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_control_ack(const sidecar_control_ack& value);
    sidecar_control_ack decode_sidecar_control_ack(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_backend_packet_manifest_snapshot(const backend_packet_manifest_snapshot& value);
    backend_packet_manifest_snapshot decode_backend_packet_manifest_snapshot(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_sidecar_manifest_snapshot_response(
        const sidecar_manifest_snapshot_response& value);
    sidecar_manifest_snapshot_response decode_sidecar_manifest_snapshot_response(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_node_auth_challenge(const node_auth_challenge& value);
    node_auth_challenge decode_node_auth_challenge(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_node_hello(const node_hello& value);
    node_hello decode_node_hello(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_direct_connect_code(const direct_connect_code& value);
    direct_connect_code decode_direct_connect_code(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_node_accepted(const node_accepted& value);
    node_accepted decode_node_accepted(std::span<const std::uint8_t> payload);

    std::vector<std::uint8_t> encode_direct_connect_code_validation_response(
        const direct_connect_code_validation_response& value);
    direct_connect_code_validation_response decode_direct_connect_code_validation_response(
        std::span<const std::uint8_t> payload);

    packet_frame create_node_auth_challenge_frame(const node_auth_challenge& value);

    gateway_direct_handshake_result complete_gateway_direct_handshake(const packet_frame& hello_frame,
                                                                      const packet_frame& direct_connect_code_frame,
                                                                      direct_connect_code_validator& validator,
                                                                      const std::string& backend_connection_id);

    bool is_routed_packet_kind(packet_kind kind) noexcept;

} // namespace cpp_backend
