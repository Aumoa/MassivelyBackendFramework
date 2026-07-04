#include "cpp_backend/packet_core.hpp"

#include <algorithm>
#include <cctype>
#include <exception>
#include <functional>
#include <iostream>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

using namespace cpp_backend;

namespace {

guid_bytes vector_guid()
{
    return {
        0x33, 0x22, 0x11, 0x00,
        0x55, 0x44,
        0x77, 0x66,
        0x88, 0x99,
        0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
    };
}

std::string to_hex(std::span<const std::uint8_t> bytes)
{
    constexpr char digits[] = "0123456789abcdef";
    std::string hex;
    hex.reserve(bytes.size() * 2);
    for (auto byte : bytes) {
        hex.push_back(digits[(byte >> 4) & 0x0F]);
        hex.push_back(digits[byte & 0x0F]);
    }

    return hex;
}

std::vector<std::uint8_t> from_hex(const std::string& hex)
{
    if ((hex.size() % 2) != 0) {
        throw std::runtime_error("Hex string length must be even.");
    }

    auto nibble = [](char value) -> std::uint8_t {
        if (value >= '0' && value <= '9') {
            return static_cast<std::uint8_t>(value - '0');
        }

        auto lower = static_cast<char>(std::tolower(static_cast<unsigned char>(value)));
        if (lower >= 'a' && lower <= 'f') {
            return static_cast<std::uint8_t>(10 + lower - 'a');
        }

        throw std::runtime_error("Invalid hex character.");
    };

    std::vector<std::uint8_t> bytes;
    bytes.reserve(hex.size() / 2);
    for (std::size_t index = 0; index < hex.size(); index += 2) {
        bytes.push_back(static_cast<std::uint8_t>((nibble(hex[index]) << 4) | nibble(hex[index + 1])));
    }

    return bytes;
}

void require(bool condition, const char* message)
{
    if (!condition) {
        throw std::runtime_error(message);
    }
}

void require_equal(const std::string& expected, const std::string& actual, const char* name)
{
    if (expected == actual) {
        return;
    }

    std::ostringstream message;
    message << name << " mismatch.\nexpected: " << expected << "\nactual:   " << actual;
    throw std::runtime_error(message.str());
}

void require_packet_error(const std::function<void()>& action, const char* name)
{
    try {
        action();
    } catch (const packet_error&) {
        return;
    }

    throw std::runtime_error(std::string(name) + " did not throw packet_error.");
}

std::string encode_frame_hex(
    packet_kind kind,
    std::uint16_t packet_id,
    std::uint16_t version,
    std::vector<std::uint8_t> payload)
{
    auto frame = make_frame(kind, packet_id, version, std::move(payload));
    return to_hex(encode_frame(frame));
}

packet_frame decode_full_frame(const std::string& hex)
{
    auto bytes = from_hex(hex);
    auto header = decode_header(
        std::span(bytes.data(), packet_header::size),
        packet_header::max_payload_length);
    require(bytes.size() == packet_header::size + header.payload_length, "Frame payload length mismatch.");
    auto payload_begin = bytes.begin() + static_cast<std::ptrdiff_t>(packet_header::size);
    std::vector<std::uint8_t> payload(payload_begin, bytes.end());
    return packet_frame { header, std::move(payload) };
}

packet_frame make_hello_frame(
    master_node_kind node_kind = master_node_kind::gateway,
    std::string gateway_node_id = "gateway-a",
    std::string gateway_master_connection_id = "gateway-master-a")
{
    node_hello hello {
        node_kind,
        master_control_schema_version,
        std::move(gateway_node_id),
        "Gateway A",
        std::move(gateway_master_connection_id),
    };

    return make_frame(
        packet_kind::control,
        master_pid_node_hello,
        master_control_schema_version,
        encode_node_hello(hello));
}

packet_frame make_direct_connect_code_frame(std::string code = "code-1")
{
    return make_frame(
        packet_kind::control,
        master_pid_direct_connect_code,
        master_control_schema_version,
        encode_direct_connect_code(direct_connect_code { std::move(code) }));
}

class recording_validator final : public direct_connect_code_validator {
public:
    direct_connect_code_validation_response response {
        vector_guid(),
        true,
        "gateway-a",
        "gateway-master-a",
        master_node_kind::backend,
        "cpp-backend",
        "backend-master-a",
        "",
    };

    std::string code;
    std::string gateway_node_id;
    std::string gateway_master_connection_id;
    int call_count = 0;

    direct_connect_code_validation_response validate(
        const std::string& requested_code,
        const std::string& requested_gateway_node_id,
        const std::string& requested_gateway_master_connection_id) override
    {
        ++call_count;
        code = requested_code;
        gateway_node_id = requested_gateway_node_id;
        gateway_master_connection_id = requested_gateway_master_connection_id;
        return response;
    }
};

class recording_frame_writer final : public gateway_frame_writer {
public:
    std::vector<packet_frame> frames;

    void write(packet_frame frame) override
    {
        frames.push_back(std::move(frame));
    }
};

void packet_core_header_matches_vector()
{
    packet_header header {
        packet_kind::control,
        0,
        master_pid_node_auth_challenge,
        master_control_schema_version,
        0,
    };

    auto bytes = encode_header(header);
    require_equal("c000640008000000", to_hex(bytes), "PacketCore header");

    auto decoded = decode_header(bytes, packet_header::max_payload_length);
    require(decoded.kind == packet_kind::control, "Decoded header kind mismatch.");
    require(decoded.packet_id == master_pid_node_auth_challenge, "Decoded header packet id mismatch.");
    require(decoded.version == master_control_schema_version, "Decoded header version mismatch.");
    require(decoded.payload_length == 0, "Decoded header payload length mismatch.");
}

void channel_open_matches_vector()
{
    gateway_backend_channel_open open {
        0x01020304,
        std::string("player-1"),
    };

    constexpr auto expected = "80000a0001000011010203040100000008706c617965722d31";
    require_equal(
        expected,
        encode_frame_hex(
            packet_kind::notify,
            pid_gate_backend_channel_open,
            gateway_backend_channel_version,
            encode_gateway_backend_channel_open(open)),
        "Gateway Backend channel open");

    auto frame = decode_full_frame(expected);
    auto decoded = decode_gateway_backend_channel_open(frame.payload);
    require(decoded.channel_id == open.channel_id, "Decoded channel open id mismatch.");
    require(decoded.principal_subject_id == open.principal_subject_id, "Decoded principal subject mismatch.");
}

void channel_data_matches_vector()
{
    gateway_backend_channel_data_envelope envelope {
        0x01020304,
        packet_kind::request,
        0x1234,
        2,
        vector_guid(),
        {0xde, 0xad, 0xbe, 0xef},
    };

    constexpr auto expected = "00000800010000220102030400123400020133221100554477668899aabbccddeeff00000004deadbeef";
    require_equal(
        expected,
        encode_frame_hex(
            packet_kind::request,
            pid_gate_backend_channel_data,
            gateway_backend_channel_version,
            encode_gateway_backend_channel_data_envelope(envelope)),
        "Gateway Backend channel data");

    auto frame = decode_full_frame(expected);
    auto decoded = decode_gateway_backend_channel_data_envelope(frame.payload);
    require(decoded.channel_id == envelope.channel_id, "Decoded channel data id mismatch.");
    require(decoded.routed_kind == envelope.routed_kind, "Decoded routed kind mismatch.");
    require(decoded.routed_packet_id == envelope.routed_packet_id, "Decoded routed packet id mismatch.");
    require(decoded.routed_version == envelope.routed_version, "Decoded routed version mismatch.");
    require(decoded.exchange_id == envelope.exchange_id, "Decoded exchange id mismatch.");
    require(decoded.routed_payload == envelope.routed_payload, "Decoded routed payload mismatch.");
}

void channel_close_matches_vector()
{
    gateway_backend_channel_close close {
        0x01020304,
        "done",
    };

    constexpr auto expected = "800009000100000c0102030400000004646f6e65";
    require_equal(
        expected,
        encode_frame_hex(
            packet_kind::notify,
            pid_gate_backend_channel_close,
            gateway_backend_channel_version,
            encode_gateway_backend_channel_close(close)),
        "Gateway Backend channel close");

    auto frame = decode_full_frame(expected);
    auto decoded = decode_gateway_backend_channel_close(frame.payload);
    require(decoded.channel_id == close.channel_id, "Decoded channel close id mismatch.");
    require(decoded.reason == close.reason, "Decoded channel close reason mismatch.");
}

void sidecar_direct_connect_validation_matches_vector()
{
    direct_connect_code_validation_request request {
        vector_guid(),
        "code-1",
        "gateway-a",
        "master-a",
    };

    constexpr auto expected = "c00001000100003333221100554477668899aabbccddeeff00000006636f64652d3100000009676174657761792d61000000086d61737465722d61";
    require_equal(
        expected,
        encode_frame_hex(
            packet_kind::control,
            sidecar_pid_direct_connect_validation_request,
            sidecar_control_schema_version,
            encode_direct_connect_code_validation_request(request)),
        "Sidecar direct-connect validation request");

    auto frame = decode_full_frame(expected);
    auto decoded = decode_direct_connect_code_validation_request(frame.payload);
    require(decoded.request_id == request.request_id, "Decoded validation request id mismatch.");
    require(decoded.code == request.code, "Decoded validation code mismatch.");
    require(decoded.gateway_node_id == request.gateway_node_id, "Decoded validation gateway node mismatch.");
    require(
        decoded.gateway_master_connection_id == request.gateway_master_connection_id,
        "Decoded validation gateway Master connection mismatch.");
}

void sidecar_manifest_snapshot_request_matches_vector()
{
    sidecar_manifest_snapshot_request request {
        vector_guid(),
    };

    constexpr auto expected = "c0000b000100001033221100554477668899aabbccddeeff";
    require_equal(
        expected,
        encode_frame_hex(
            packet_kind::control,
            sidecar_pid_manifest_snapshot_request,
            sidecar_control_schema_version,
            encode_sidecar_manifest_snapshot_request(request)),
        "Sidecar manifest snapshot request");

    auto frame = decode_full_frame(expected);
    auto decoded = decode_sidecar_manifest_snapshot_request(frame.payload);
    require(decoded.request_id == request.request_id, "Decoded manifest snapshot request id mismatch.");
}

void node_auth_challenge_round_trips()
{
    node_auth_challenge challenge;
    challenge.challenge_id = "challenge-a";
    for (std::size_t index = 0; index < challenge.nonce.size(); ++index) {
        challenge.nonce[index] = static_cast<std::uint8_t>(index);
    }

    auto frame = create_node_auth_challenge_frame(challenge);
    require(frame.header.kind == packet_kind::control, "Challenge frame kind mismatch.");
    require(frame.header.packet_id == master_pid_node_auth_challenge, "Challenge frame packet id mismatch.");
    require(frame.header.version == master_control_schema_version, "Challenge frame version mismatch.");

    auto decoded = decode_node_auth_challenge(frame.payload);
    require(decoded.challenge_id == challenge.challenge_id, "Decoded challenge id mismatch.");
    require(decoded.nonce == challenge.nonce, "Decoded challenge nonce mismatch.");
}

void gateway_direct_handshake_accepts_valid_gateway()
{
    recording_validator validator;
    auto result = complete_gateway_direct_handshake(
        make_hello_frame(),
        make_direct_connect_code_frame(),
        validator,
        "backend-connection-a");

    require(validator.call_count == 1, "Validator call count mismatch.");
    require(validator.code == "code-1", "Validator code mismatch.");
    require(validator.gateway_node_id == "gateway-a", "Validator gateway node mismatch.");
    require(
        validator.gateway_master_connection_id == "gateway-master-a",
        "Validator gateway Master connection mismatch.");
    require(result.gateway_node_id == "gateway-a", "Handshake result gateway node mismatch.");
    require(
        result.gateway_master_connection_id == "gateway-master-a",
        "Handshake result gateway Master connection mismatch.");
    require(result.accepted_frame.header.kind == packet_kind::control, "Accepted frame kind mismatch.");
    require(result.accepted_frame.header.packet_id == master_pid_node_accepted, "Accepted frame packet id mismatch.");

    auto accepted = decode_node_accepted(result.accepted_frame.payload);
    require(accepted.node_id == "gateway-a", "Accepted node id mismatch.");
    require(accepted.connection_id == "backend-connection-a", "Accepted connection id mismatch.");
}

void gateway_direct_handshake_rejects_failed_validation()
{
    recording_validator validator;
    validator.response.success = false;
    validator.response.error_message = "invalid direct-connect code";

    require_packet_error(
        [&validator] {
            (void)complete_gateway_direct_handshake(
                make_hello_frame(),
                make_direct_connect_code_frame(),
                validator,
                "backend-connection-a");
        },
        "Failed validation handshake");
}

void gateway_direct_handshake_rejects_unexpected_validation_identity()
{
    recording_validator validator;
    validator.response.target_node_kind = master_node_kind::dedicated;

    require_packet_error(
        [&validator] {
            (void)complete_gateway_direct_handshake(
                make_hello_frame(),
                make_direct_connect_code_frame(),
                validator,
                "backend-connection-a");
        },
        "Unexpected validation identity handshake");
}

void gateway_direct_handshake_rejects_non_gateway_hello()
{
    recording_validator validator;

    require_packet_error(
        [&validator] {
            (void)complete_gateway_direct_handshake(
                make_hello_frame(master_node_kind::backend),
                make_direct_connect_code_frame(),
                validator,
                "backend-connection-a");
        },
        "Non-Gateway hello handshake");
    require(validator.call_count == 0, "Validator should not run for non-Gateway hello.");
}

void trusted_gateway_session_tracks_open_data_and_close()
{
    trusted_gateway_session session("gateway-a", "gateway-master-a");
    auto open_frame = make_frame(
        packet_kind::notify,
        pid_gate_backend_channel_open,
        gateway_backend_channel_version,
        encode_gateway_backend_channel_open(gateway_backend_channel_open {
            37,
            std::string("player-1"),
        }));

    auto open_event = session.handle_gateway_frame(open_frame);
    require(open_event.kind == gateway_session_event_kind::channel_open, "Open event kind mismatch.");
    require(open_event.open.has_value(), "Open event missing payload.");
    require(open_event.open->channel_id == 37, "Open event channel id mismatch.");
    require(session.has_channel(37), "Session did not track opened channel.");

    auto data_frame = make_frame(
        packet_kind::request,
        pid_gate_backend_channel_data,
        gateway_backend_channel_version,
        encode_gateway_backend_channel_data_envelope(gateway_backend_channel_data_envelope {
            37,
            packet_kind::request,
            101,
            1,
            vector_guid(),
            {0x01, 0x02},
        }));
    auto data_event = session.handle_gateway_frame(data_frame);
    require(data_event.kind == gateway_session_event_kind::channel_data, "Data event kind mismatch.");
    require(data_event.data.has_value(), "Data event missing payload.");
    require(data_event.data->channel_id == 37, "Data event channel id mismatch.");
    require(data_event.data->routed_payload == std::vector<std::uint8_t>({0x01, 0x02}), "Data payload mismatch.");

    auto close_frame = make_frame(
        packet_kind::notify,
        pid_gate_backend_channel_close,
        gateway_backend_channel_version,
        encode_gateway_backend_channel_close(gateway_backend_channel_close {
            37,
            "client closed",
        }));
    auto close_event = session.handle_gateway_frame(close_frame);
    require(close_event.kind == gateway_session_event_kind::channel_close, "Close event kind mismatch.");
    require(close_event.close.has_value(), "Close event missing payload.");
    require(!session.has_channel(37), "Session did not remove closed channel.");
}

void trusted_gateway_session_rejects_unknown_channel_data()
{
    trusted_gateway_session session("gateway-a", "gateway-master-a");
    auto data_frame = make_frame(
        packet_kind::notify,
        pid_gate_backend_channel_data,
        gateway_backend_channel_version,
        encode_gateway_backend_channel_data_envelope(gateway_backend_channel_data_envelope {
            37,
            packet_kind::notify,
            201,
            1,
            std::nullopt,
            {0x01},
        }));

    require_packet_error(
        [&session, &data_frame] {
            (void)session.handle_gateway_frame(data_frame);
        },
        "Unknown channel data");
}

void trusted_gateway_session_writes_server_origin_frames()
{
    trusted_gateway_session session("gateway-a", "gateway-master-a");
    auto open_frame = make_frame(
        packet_kind::notify,
        pid_gate_backend_channel_open,
        gateway_backend_channel_version,
        encode_gateway_backend_channel_open(gateway_backend_channel_open {
            37,
            std::nullopt,
        }));
    (void)session.handle_gateway_frame(open_frame);

    recording_frame_writer writer;
    session.write_channel_data(
        writer,
        37,
        packet_kind::notify,
        201,
        1,
        std::nullopt,
        {0xaa, 0xbb});

    require(writer.frames.size() == 1, "Expected one server-origin data frame.");
    require(writer.frames[0].header.kind == packet_kind::notify, "Server-origin data frame kind mismatch.");
    require(writer.frames[0].header.packet_id == pid_gate_backend_channel_data, "Server-origin data packet id mismatch.");
    auto data = decode_gateway_backend_channel_data_envelope(writer.frames[0].payload);
    require(data.channel_id == 37, "Server-origin data channel id mismatch.");
    require(data.routed_payload == std::vector<std::uint8_t>({0xaa, 0xbb}), "Server-origin data payload mismatch.");

    session.write_channel_close(writer, 37, "backend closed");
    require(writer.frames.size() == 2, "Expected close frame.");
    require(writer.frames[1].header.kind == packet_kind::notify, "Server-origin close frame kind mismatch.");
    require(writer.frames[1].header.packet_id == pid_gate_backend_channel_close, "Server-origin close packet id mismatch.");
    auto close = decode_gateway_backend_channel_close(writer.frames[1].payload);
    require(close.channel_id == 37, "Server-origin close channel id mismatch.");
    require(close.reason == "backend closed", "Server-origin close reason mismatch.");
    require(!session.has_channel(37), "Server-origin close did not remove channel.");
}

void trusted_gateway_session_clears_channels_on_disconnect()
{
    trusted_gateway_session session("gateway-a", "gateway-master-a");
    auto open_frame = make_frame(
        packet_kind::notify,
        pid_gate_backend_channel_open,
        gateway_backend_channel_version,
        encode_gateway_backend_channel_open(gateway_backend_channel_open {
            37,
            std::nullopt,
        }));
    (void)session.handle_gateway_frame(open_frame);
    require(session.channel_count() == 1, "Expected one tracked channel.");

    session.mark_disconnected();
    require(session.channel_count() == 0, "Disconnect did not clear channels.");
}

} // namespace

int main()
{
    try {
        packet_core_header_matches_vector();
        channel_open_matches_vector();
        channel_data_matches_vector();
        channel_close_matches_vector();
        sidecar_direct_connect_validation_matches_vector();
        sidecar_manifest_snapshot_request_matches_vector();
        node_auth_challenge_round_trips();
        gateway_direct_handshake_accepts_valid_gateway();
        gateway_direct_handshake_rejects_failed_validation();
        gateway_direct_handshake_rejects_unexpected_validation_identity();
        gateway_direct_handshake_rejects_non_gateway_hello();
        trusted_gateway_session_tracks_open_data_and_close();
        trusted_gateway_session_rejects_unknown_channel_data();
        trusted_gateway_session_writes_server_origin_frames();
        trusted_gateway_session_clears_channels_on_disconnect();
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }

    return 0;
}
