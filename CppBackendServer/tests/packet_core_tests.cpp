#include "cpp_backend/packet_core.hpp"

#include <algorithm>
#include <cctype>
#include <exception>
#include <iostream>
#include <sstream>
#include <string>
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
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }

    return 0;
}
