#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

namespace cpp_backend {

class packet_error : public std::runtime_error {
public:
    explicit packet_error(const std::string& message);
};

enum class packet_kind : std::uint8_t {
    request = 0,
    response = 1,
    notify = 2,
    control = 3,
};

using guid_bytes = std::array<std::uint8_t, 16>;

struct packet_header {
    static constexpr std::size_t size = 8;
    static constexpr std::uint32_t max_payload_length = 0xFFFFFF;

    packet_kind kind = packet_kind::control;
    std::uint8_t flags = 0;
    std::uint16_t packet_id = 0;
    std::uint16_t version = 0;
    std::uint32_t payload_length = 0;
};

struct packet_frame {
    packet_header header;
    std::vector<std::uint8_t> payload;
};

class packet_writer {
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

class packet_reader {
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
packet_frame make_frame(
    packet_kind kind,
    std::uint16_t packet_id,
    std::uint16_t version,
    std::vector<std::uint8_t> payload,
    std::uint8_t flags = 0);

constexpr std::uint16_t master_control_schema_version = 8;
constexpr std::uint16_t master_pid_node_auth_challenge = 100;

constexpr std::uint16_t gateway_backend_channel_version = 1;
constexpr std::uint16_t pid_gate_backend_channel_data = 8;
constexpr std::uint16_t pid_gate_backend_channel_close = 9;
constexpr std::uint16_t pid_gate_backend_channel_open = 10;

constexpr std::uint16_t sidecar_control_schema_version = 1;
constexpr std::uint16_t sidecar_pid_direct_connect_validation_request = 1;
constexpr std::uint16_t sidecar_pid_manifest_snapshot_request = 11;

struct gateway_backend_channel_open {
    std::uint32_t channel_id = 0;
    std::optional<std::string> principal_subject_id;
};

struct gateway_backend_channel_data_envelope {
    std::uint32_t channel_id = 0;
    packet_kind routed_kind = packet_kind::request;
    std::uint16_t routed_packet_id = 0;
    std::uint16_t routed_version = 0;
    std::optional<guid_bytes> exchange_id;
    std::vector<std::uint8_t> routed_payload;
};

struct gateway_backend_channel_close {
    std::uint32_t channel_id = 0;
    std::string reason;
};

struct direct_connect_code_validation_request {
    guid_bytes request_id {};
    std::string code;
    std::string gateway_node_id;
    std::string gateway_master_connection_id;
};

struct sidecar_manifest_snapshot_request {
    guid_bytes request_id {};
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

std::vector<std::uint8_t> encode_sidecar_manifest_snapshot_request(
    const sidecar_manifest_snapshot_request& value);
sidecar_manifest_snapshot_request decode_sidecar_manifest_snapshot_request(
    std::span<const std::uint8_t> payload);

bool is_routed_packet_kind(packet_kind kind) noexcept;

} // namespace cpp_backend
