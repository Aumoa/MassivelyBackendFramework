#include "cpp_backend/packet_core.hpp"

#include <algorithm>
#include <cstring>
#include <limits>
#include <random>
#include <string>
#include <utility>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <cerrno>
#include <netdb.h>
#include <sys/socket.h>
#include <unistd.h>
#endif

namespace cpp_backend {

namespace {

constexpr std::uint8_t kind_shift = 6;
constexpr std::uint8_t flags_mask = 0x3F;

#ifdef _WIN32
using socket_handle = SOCKET;
constexpr socket_handle invalid_socket_handle = INVALID_SOCKET;
#else
using socket_handle = int;
constexpr socket_handle invalid_socket_handle = -1;
#endif

bool is_valid_kind(packet_kind kind) noexcept
{
    return kind == packet_kind::request ||
           kind == packet_kind::response ||
           kind == packet_kind::notify ||
           kind == packet_kind::control;
}

void validate_header(const packet_header& header)
{
    if (!is_valid_kind(header.kind)) {
        throw packet_error("Invalid packet kind.");
    }

    if ((header.flags & ~flags_mask) != 0) {
        throw packet_error("Invalid packet flags.");
    }

    if (header.packet_id == 0) {
        throw packet_error("Packet id must be non-zero.");
    }

    if (header.version == 0) {
        throw packet_error("Packet version must be non-zero.");
    }

    if (header.payload_length > packet_header::max_payload_length) {
        throw packet_error("Packet payload is too large.");
    }
}

void require_non_zero(std::uint32_t value, const char* name)
{
    if (value == 0) {
        throw packet_error(std::string(name) + " must be non-zero.");
    }
}

void require_non_empty(const std::string& value, const char* name)
{
    if (value.empty()) {
        throw packet_error(std::string(name) + " is required.");
    }
}

bool is_valid_master_node_kind(master_node_kind kind) noexcept
{
    return kind == master_node_kind::gateway ||
           kind == master_node_kind::dedicated ||
           kind == master_node_kind::master_admin ||
           kind == master_node_kind::backend;
}

void require_control_frame(
    const packet_frame& frame,
    std::uint16_t packet_id,
    std::uint16_t version)
{
    if (frame.header.kind != packet_kind::control ||
        frame.header.flags != 0 ||
        frame.header.packet_id != packet_id ||
        frame.header.version != version ||
        frame.header.payload_length != frame.payload.size()) {
        throw packet_error("Unexpected control frame.");
    }
}

class socket_runtime {
public:
    socket_runtime()
    {
#ifdef _WIN32
        WSADATA data {};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
            throw packet_error("WSAStartup failed.");
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

class socket_owner {
public:
    socket_owner() = default;

    explicit socket_owner(socket_handle handle)
        : m_handle(handle)
    {
    }

    socket_owner(const socket_owner&) = delete;
    socket_owner& operator=(const socket_owner&) = delete;

    socket_owner(socket_owner&& other) noexcept
        : m_handle(other.m_handle)
    {
        other.m_handle = invalid_socket_handle;
    }

    socket_owner& operator=(socket_owner&& other) noexcept
    {
        if (this != &other) {
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
        if (m_handle != invalid_socket_handle) {
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

std::string socket_error_message(const std::string& prefix)
{
#ifdef _WIN32
    return prefix + " WSAError=" + std::to_string(WSAGetLastError());
#else
    return prefix + " errno=" + std::to_string(errno);
#endif
}

socket_owner connect_tcp(const sidecar_control_endpoint& endpoint)
{
    if (endpoint.host.empty()) {
        throw packet_error("Sidecar host is required.");
    }

    if (endpoint.port == 0) {
        throw packet_error("Sidecar port is required.");
    }

    static socket_runtime runtime;

    addrinfo hints {};
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;
    hints.ai_protocol = IPPROTO_TCP;

    addrinfo* results = nullptr;
    auto port = std::to_string(endpoint.port);
    int resolve_result = getaddrinfo(endpoint.host.c_str(), port.c_str(), &hints, &results);
    if (resolve_result != 0) {
        throw packet_error("Failed to resolve sidecar endpoint.");
    }

    struct addrinfo_owner {
        addrinfo* value = nullptr;
        ~addrinfo_owner()
        {
            if (value != nullptr) {
                freeaddrinfo(value);
            }
        }
    } result_owner { results };

    for (auto* current = results; current != nullptr; current = current->ai_next) {
        socket_owner socket(::socket(current->ai_family, current->ai_socktype, current->ai_protocol));
        if (!socket) {
            continue;
        }

        if (::connect(socket.get(), current->ai_addr, static_cast<int>(current->ai_addrlen)) == 0) {
            return socket;
        }
    }

    throw packet_error(socket_error_message("Failed to connect to sidecar endpoint."));
}

void send_all(socket_handle socket, std::span<const std::uint8_t> bytes)
{
    std::size_t sent = 0;
    while (sent < bytes.size()) {
        auto remaining = bytes.size() - sent;
        auto chunk_size = remaining > static_cast<std::size_t>(std::numeric_limits<int>::max())
            ? std::numeric_limits<int>::max()
            : static_cast<int>(remaining);
#ifdef _WIN32
        int result = ::send(
            socket,
            reinterpret_cast<const char*>(bytes.data() + sent),
            chunk_size,
            0);
#else
        int result = static_cast<int>(::send(
            socket,
            bytes.data() + sent,
            static_cast<std::size_t>(chunk_size),
            0));
#endif
        if (result <= 0) {
            throw packet_error(socket_error_message("Socket send failed."));
        }

        sent += static_cast<std::size_t>(result);
    }
}

void receive_exact(socket_handle socket, std::span<std::uint8_t> bytes)
{
    std::size_t received = 0;
    while (received < bytes.size()) {
        auto remaining = bytes.size() - received;
        auto chunk_size = remaining > static_cast<std::size_t>(std::numeric_limits<int>::max())
            ? std::numeric_limits<int>::max()
            : static_cast<int>(remaining);
#ifdef _WIN32
        int result = ::recv(
            socket,
            reinterpret_cast<char*>(bytes.data() + received),
            chunk_size,
            0);
#else
        int result = static_cast<int>(::recv(
            socket,
            bytes.data() + received,
            static_cast<std::size_t>(chunk_size),
            0));
#endif
        if (result <= 0) {
            throw packet_error(socket_error_message("Socket receive failed."));
        }

        received += static_cast<std::size_t>(result);
    }
}

void write_socket_frame(socket_handle socket, const packet_frame& frame)
{
    auto bytes = encode_frame(frame);
    send_all(socket, bytes);
}

packet_frame read_socket_frame(socket_handle socket, std::uint32_t max_payload_length)
{
    std::array<std::uint8_t, packet_header::size> header_bytes {};
    receive_exact(socket, header_bytes);
    auto header = decode_header(header_bytes, max_payload_length);
    std::vector<std::uint8_t> payload(header.payload_length);
    if (!payload.empty()) {
        receive_exact(socket, payload);
    }

    return packet_frame { header, std::move(payload) };
}

guid_bytes create_request_id()
{
    std::random_device random;
    guid_bytes value {};
    for (auto& byte : value) {
        byte = static_cast<std::uint8_t>(random());
    }

    if (std::all_of(value.begin(), value.end(), [](std::uint8_t byte) { return byte == 0; })) {
        value.back() = 1;
    }

    return value;
}

} // namespace

packet_error::packet_error(const std::string& message)
    : std::runtime_error(message)
{
}

sidecar_direct_connect_code_validator::sidecar_direct_connect_code_validator(sidecar_control_endpoint endpoint)
    : m_endpoint(std::move(endpoint))
{
}

direct_connect_code_validation_response sidecar_direct_connect_code_validator::validate(
    const std::string& code,
    const std::string& gateway_node_id,
    const std::string& gateway_master_connection_id)
{
    direct_connect_code_validation_request request {
        create_request_id(),
        code,
        gateway_node_id,
        gateway_master_connection_id,
    };

    auto socket = connect_tcp(m_endpoint);
    auto request_frame = make_frame(
        packet_kind::control,
        sidecar_pid_direct_connect_validation_request,
        sidecar_control_schema_version,
        encode_direct_connect_code_validation_request(request));
    write_socket_frame(socket.get(), request_frame);

    auto response_frame = read_socket_frame(socket.get(), sidecar_control_max_payload_length);
    require_control_frame(
        response_frame,
        sidecar_pid_direct_connect_validation_response,
        sidecar_control_schema_version);

    auto response = decode_direct_connect_code_validation_response(response_frame.payload);
    if (response.request_id != request.request_id) {
        throw packet_error("Sidecar validation response used a different request id.");
    }

    return response;
}

void packet_writer::write_byte(std::uint8_t value)
{
    m_bytes.push_back(value);
}

void packet_writer::write_uint16(std::uint16_t value)
{
    m_bytes.push_back(static_cast<std::uint8_t>((value >> 8) & 0xFF));
    m_bytes.push_back(static_cast<std::uint8_t>(value & 0xFF));
}

void packet_writer::write_uint32(std::uint32_t value)
{
    m_bytes.push_back(static_cast<std::uint8_t>((value >> 24) & 0xFF));
    m_bytes.push_back(static_cast<std::uint8_t>((value >> 16) & 0xFF));
    m_bytes.push_back(static_cast<std::uint8_t>((value >> 8) & 0xFF));
    m_bytes.push_back(static_cast<std::uint8_t>(value & 0xFF));
}

void packet_writer::write_int32(std::int32_t value)
{
    write_uint32(static_cast<std::uint32_t>(value));
}

void packet_writer::write_int64(std::int64_t value)
{
    auto unsigned_value = static_cast<std::uint64_t>(value);
    for (int shift = 56; shift >= 0; shift -= 8) {
        m_bytes.push_back(static_cast<std::uint8_t>((unsigned_value >> shift) & 0xFF));
    }
}

void packet_writer::write_guid(const guid_bytes& value)
{
    write_bytes(value);
}

void packet_writer::write_string(const std::string& value)
{
    if (value.size() > static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max())) {
        throw packet_error("String is too large.");
    }

    write_int32(static_cast<std::int32_t>(value.size()));
    write_bytes(std::span(
        reinterpret_cast<const std::uint8_t*>(value.data()),
        value.size()));
}

void packet_writer::write_bytes(std::span<const std::uint8_t> value)
{
    m_bytes.insert(m_bytes.end(), value.begin(), value.end());
}

const std::vector<std::uint8_t>& packet_writer::bytes() const noexcept
{
    return m_bytes;
}

std::vector<std::uint8_t> packet_writer::take_bytes()
{
    return std::move(m_bytes);
}

packet_reader::packet_reader(std::span<const std::uint8_t> bytes)
    : m_bytes(bytes)
{
}

std::uint8_t packet_reader::read_byte()
{
    ensure_available(1);
    return m_bytes[m_position++];
}

std::uint16_t packet_reader::read_uint16()
{
    ensure_available(2);
    auto value = static_cast<std::uint16_t>(
        (static_cast<std::uint16_t>(m_bytes[m_position]) << 8) |
        static_cast<std::uint16_t>(m_bytes[m_position + 1]));
    m_position += 2;
    return value;
}

std::uint32_t packet_reader::read_uint32()
{
    ensure_available(4);
    std::uint32_t value =
        (static_cast<std::uint32_t>(m_bytes[m_position]) << 24) |
        (static_cast<std::uint32_t>(m_bytes[m_position + 1]) << 16) |
        (static_cast<std::uint32_t>(m_bytes[m_position + 2]) << 8) |
        static_cast<std::uint32_t>(m_bytes[m_position + 3]);
    m_position += 4;
    return value;
}

std::int32_t packet_reader::read_int32()
{
    return static_cast<std::int32_t>(read_uint32());
}

std::int64_t packet_reader::read_int64()
{
    ensure_available(8);
    std::uint64_t value = 0;
    for (int index = 0; index < 8; ++index) {
        value = (value << 8) | static_cast<std::uint64_t>(m_bytes[m_position + index]);
    }

    m_position += 8;
    return static_cast<std::int64_t>(value);
}

guid_bytes packet_reader::read_guid()
{
    ensure_available(16);
    guid_bytes value {};
    std::copy_n(m_bytes.begin() + static_cast<std::ptrdiff_t>(m_position), value.size(), value.begin());
    m_position += value.size();
    return value;
}

std::string packet_reader::read_string()
{
    auto byte_count = read_int32();
    if (byte_count < 0) {
        throw packet_error("String length cannot be negative.");
    }

    ensure_available(static_cast<std::size_t>(byte_count));
    auto data = reinterpret_cast<const char*>(m_bytes.data() + m_position);
    std::string value(data, static_cast<std::size_t>(byte_count));
    m_position += static_cast<std::size_t>(byte_count);
    return value;
}

std::vector<std::uint8_t> packet_reader::read_bytes(std::size_t length)
{
    ensure_available(length);
    auto begin = m_bytes.begin() + static_cast<std::ptrdiff_t>(m_position);
    auto end = begin + static_cast<std::ptrdiff_t>(length);
    std::vector<std::uint8_t> value(begin, end);
    m_position += length;
    return value;
}

void packet_reader::require_finished() const
{
    if (remaining() != 0) {
        throw packet_error("Packet payload has trailing bytes.");
    }
}

std::size_t packet_reader::remaining() const noexcept
{
    return m_bytes.size() - m_position;
}

void packet_reader::ensure_available(std::size_t count) const
{
    if (remaining() < count) {
        throw packet_error("Packet payload is too small.");
    }
}

std::array<std::uint8_t, packet_header::size> encode_header(const packet_header& header)
{
    validate_header(header);

    std::array<std::uint8_t, packet_header::size> bytes {};
    bytes[0] = static_cast<std::uint8_t>(
        (static_cast<std::uint8_t>(header.kind) << kind_shift) |
        (header.flags & flags_mask));
    bytes[1] = static_cast<std::uint8_t>((header.packet_id >> 8) & 0xFF);
    bytes[2] = static_cast<std::uint8_t>(header.packet_id & 0xFF);
    bytes[3] = static_cast<std::uint8_t>((header.version >> 8) & 0xFF);
    bytes[4] = static_cast<std::uint8_t>(header.version & 0xFF);
    bytes[5] = static_cast<std::uint8_t>((header.payload_length >> 16) & 0xFF);
    bytes[6] = static_cast<std::uint8_t>((header.payload_length >> 8) & 0xFF);
    bytes[7] = static_cast<std::uint8_t>(header.payload_length & 0xFF);
    return bytes;
}

packet_header decode_header(std::span<const std::uint8_t> bytes, std::uint32_t max_payload_length)
{
    if (bytes.size() < packet_header::size) {
        throw packet_error("Packet header is too small.");
    }

    auto kind = static_cast<packet_kind>(bytes[0] >> kind_shift);
    auto flags = static_cast<std::uint8_t>(bytes[0] & flags_mask);
    auto packet_id = static_cast<std::uint16_t>(
        (static_cast<std::uint16_t>(bytes[1]) << 8) |
        static_cast<std::uint16_t>(bytes[2]));
    auto version = static_cast<std::uint16_t>(
        (static_cast<std::uint16_t>(bytes[3]) << 8) |
        static_cast<std::uint16_t>(bytes[4]));
    auto payload_length =
        (static_cast<std::uint32_t>(bytes[5]) << 16) |
        (static_cast<std::uint32_t>(bytes[6]) << 8) |
        static_cast<std::uint32_t>(bytes[7]);

    if (payload_length > max_payload_length) {
        throw packet_error("Packet payload exceeds read policy.");
    }

    packet_header header {
        kind,
        flags,
        packet_id,
        version,
        payload_length,
    };
    validate_header(header);
    return header;
}

std::vector<std::uint8_t> encode_frame(const packet_frame& frame)
{
    if (frame.payload.size() != frame.header.payload_length) {
        throw packet_error("Payload length does not match header.");
    }

    auto header_bytes = encode_header(frame.header);
    std::vector<std::uint8_t> bytes;
    bytes.reserve(header_bytes.size() + frame.payload.size());
    bytes.insert(bytes.end(), header_bytes.begin(), header_bytes.end());
    bytes.insert(bytes.end(), frame.payload.begin(), frame.payload.end());
    return bytes;
}

packet_frame make_frame(
    packet_kind kind,
    std::uint16_t packet_id,
    std::uint16_t version,
    std::vector<std::uint8_t> payload,
    std::uint8_t flags)
{
    if (payload.size() > packet_header::max_payload_length) {
        throw packet_error("Payload is too large.");
    }

    packet_header header {
        kind,
        flags,
        packet_id,
        version,
        static_cast<std::uint32_t>(payload.size()),
    };
    validate_header(header);
    return packet_frame { header, std::move(payload) };
}

std::vector<std::uint8_t> encode_gateway_backend_channel_open(const gateway_backend_channel_open& value)
{
    require_non_zero(value.channel_id, "Channel id");

    packet_writer writer;
    writer.write_uint32(value.channel_id);
    if (value.principal_subject_id.has_value()) {
        writer.write_byte(1);
        writer.write_string(*value.principal_subject_id);
    } else {
        writer.write_byte(0);
    }

    return writer.take_bytes();
}

gateway_backend_channel_open decode_gateway_backend_channel_open(std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    gateway_backend_channel_open value;
    value.channel_id = reader.read_uint32();
    require_non_zero(value.channel_id, "Channel id");
    if (reader.read_byte() != 0) {
        value.principal_subject_id = reader.read_string();
    }

    reader.require_finished();
    return value;
}

std::vector<std::uint8_t> encode_gateway_backend_channel_data_envelope(
    const gateway_backend_channel_data_envelope& value)
{
    require_non_zero(value.channel_id, "Channel id");
    if (!is_routed_packet_kind(value.routed_kind)) {
        throw packet_error("Invalid routed packet kind.");
    }

    require_non_zero(value.routed_packet_id, "Routed packet id");
    require_non_zero(value.routed_version, "Routed version");
    if ((value.routed_kind == packet_kind::request || value.routed_kind == packet_kind::response) &&
        !value.exchange_id.has_value()) {
        throw packet_error("Request and response channel data require an exchange id.");
    }

    if (value.routed_payload.size() > packet_header::max_payload_length) {
        throw packet_error("Routed payload is too large.");
    }

    packet_writer writer;
    writer.write_uint32(value.channel_id);
    writer.write_byte(static_cast<std::uint8_t>(value.routed_kind));
    writer.write_uint16(value.routed_packet_id);
    writer.write_uint16(value.routed_version);
    if (value.exchange_id.has_value()) {
        writer.write_byte(1);
        writer.write_guid(*value.exchange_id);
    } else {
        writer.write_byte(0);
    }

    writer.write_int32(static_cast<std::int32_t>(value.routed_payload.size()));
    writer.write_bytes(value.routed_payload);
    return writer.take_bytes();
}

gateway_backend_channel_data_envelope decode_gateway_backend_channel_data_envelope(
    std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    gateway_backend_channel_data_envelope value;
    value.channel_id = reader.read_uint32();
    value.routed_kind = static_cast<packet_kind>(reader.read_byte());
    value.routed_packet_id = reader.read_uint16();
    value.routed_version = reader.read_uint16();
    if (reader.read_byte() != 0) {
        value.exchange_id = reader.read_guid();
    }

    auto routed_payload_length = reader.read_int32();
    if (routed_payload_length < 0) {
        throw packet_error("Routed payload length cannot be negative.");
    }

    value.routed_payload = reader.read_bytes(static_cast<std::size_t>(routed_payload_length));
    reader.require_finished();

    require_non_zero(value.channel_id, "Channel id");
    if (!is_routed_packet_kind(value.routed_kind)) {
        throw packet_error("Invalid routed packet kind.");
    }

    require_non_zero(value.routed_packet_id, "Routed packet id");
    require_non_zero(value.routed_version, "Routed version");
    if ((value.routed_kind == packet_kind::request || value.routed_kind == packet_kind::response) &&
        !value.exchange_id.has_value()) {
        throw packet_error("Request and response channel data require an exchange id.");
    }

    return value;
}

std::vector<std::uint8_t> encode_gateway_backend_channel_close(const gateway_backend_channel_close& value)
{
    require_non_zero(value.channel_id, "Channel id");

    packet_writer writer;
    writer.write_uint32(value.channel_id);
    writer.write_string(value.reason);
    return writer.take_bytes();
}

gateway_backend_channel_close decode_gateway_backend_channel_close(std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    gateway_backend_channel_close value;
    value.channel_id = reader.read_uint32();
    value.reason = reader.read_string();
    reader.require_finished();
    require_non_zero(value.channel_id, "Channel id");
    return value;
}

std::vector<std::uint8_t> encode_direct_connect_code_validation_request(
    const direct_connect_code_validation_request& value)
{
    require_non_empty(value.code, "Direct connect code");
    require_non_empty(value.gateway_node_id, "Gateway node id");
    require_non_empty(value.gateway_master_connection_id, "Gateway Master connection id");

    packet_writer writer;
    writer.write_guid(value.request_id);
    writer.write_string(value.code);
    writer.write_string(value.gateway_node_id);
    writer.write_string(value.gateway_master_connection_id);
    return writer.take_bytes();
}

direct_connect_code_validation_request decode_direct_connect_code_validation_request(
    std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    direct_connect_code_validation_request value;
    value.request_id = reader.read_guid();
    value.code = reader.read_string();
    value.gateway_node_id = reader.read_string();
    value.gateway_master_connection_id = reader.read_string();
    reader.require_finished();

    require_non_empty(value.code, "Direct connect code");
    require_non_empty(value.gateway_node_id, "Gateway node id");
    require_non_empty(value.gateway_master_connection_id, "Gateway Master connection id");
    return value;
}

std::vector<std::uint8_t> encode_sidecar_manifest_snapshot_request(
    const sidecar_manifest_snapshot_request& value)
{
    packet_writer writer;
    writer.write_guid(value.request_id);
    return writer.take_bytes();
}

sidecar_manifest_snapshot_request decode_sidecar_manifest_snapshot_request(
    std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    sidecar_manifest_snapshot_request value;
    value.request_id = reader.read_guid();
    reader.require_finished();
    return value;
}

std::vector<std::uint8_t> encode_node_auth_challenge(const node_auth_challenge& value)
{
    require_non_empty(value.challenge_id, "Challenge id");

    packet_writer writer;
    writer.write_string(value.challenge_id);
    writer.write_int32(static_cast<std::int32_t>(value.nonce.size()));
    writer.write_bytes(value.nonce);
    return writer.take_bytes();
}

node_auth_challenge decode_node_auth_challenge(std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    node_auth_challenge value;
    value.challenge_id = reader.read_string();
    auto nonce_length = reader.read_int32();
    if (nonce_length != static_cast<std::int32_t>(master_auth_nonce_length)) {
        throw packet_error("Invalid node authentication nonce length.");
    }

    auto nonce = reader.read_bytes(master_auth_nonce_length);
    std::copy(nonce.begin(), nonce.end(), value.nonce.begin());
    reader.require_finished();
    require_non_empty(value.challenge_id, "Challenge id");
    return value;
}

std::vector<std::uint8_t> encode_node_hello(const node_hello& value)
{
    if (!is_valid_master_node_kind(value.node_kind)) {
        throw packet_error("Invalid node kind.");
    }

    require_non_zero(value.protocol_version, "Protocol version");
    require_non_empty(value.node_id, "Node id");

    packet_writer writer;
    writer.write_byte(static_cast<std::uint8_t>(value.node_kind));
    writer.write_uint16(value.protocol_version);
    writer.write_string(value.node_id);
    writer.write_string(value.display_name);
    writer.write_string(value.master_connection_id);
    return writer.take_bytes();
}

node_hello decode_node_hello(std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    node_hello value;
    value.node_kind = static_cast<master_node_kind>(reader.read_byte());
    value.protocol_version = reader.read_uint16();
    value.node_id = reader.read_string();
    value.display_name = reader.read_string();
    value.master_connection_id = reader.read_string();
    reader.require_finished();

    if (!is_valid_master_node_kind(value.node_kind)) {
        throw packet_error("Invalid node kind.");
    }

    require_non_zero(value.protocol_version, "Protocol version");
    require_non_empty(value.node_id, "Node id");
    return value;
}

std::vector<std::uint8_t> encode_direct_connect_code(const direct_connect_code& value)
{
    require_non_empty(value.code, "Direct connect code");

    packet_writer writer;
    writer.write_string(value.code);
    return writer.take_bytes();
}

direct_connect_code decode_direct_connect_code(std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    direct_connect_code value;
    value.code = reader.read_string();
    reader.require_finished();
    require_non_empty(value.code, "Direct connect code");
    return value;
}

std::vector<std::uint8_t> encode_node_accepted(const node_accepted& value)
{
    require_non_empty(value.node_id, "Node id");
    require_non_empty(value.connection_id, "Connection id");

    packet_writer writer;
    writer.write_string(value.node_id);
    writer.write_string(value.connection_id);
    return writer.take_bytes();
}

node_accepted decode_node_accepted(std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    node_accepted value;
    value.node_id = reader.read_string();
    value.connection_id = reader.read_string();
    reader.require_finished();
    require_non_empty(value.node_id, "Node id");
    require_non_empty(value.connection_id, "Connection id");
    return value;
}

std::vector<std::uint8_t> encode_direct_connect_code_validation_response(
    const direct_connect_code_validation_response& value)
{
    packet_writer writer;
    writer.write_guid(value.request_id);
    writer.write_byte(value.success ? 1 : 0);
    writer.write_string(value.gateway_node_id);
    writer.write_string(value.gateway_master_connection_id);
    writer.write_byte(static_cast<std::uint8_t>(value.target_node_kind));
    writer.write_string(value.target_node_id);
    writer.write_string(value.target_master_connection_id);
    writer.write_string(value.error_message);
    return writer.take_bytes();
}

direct_connect_code_validation_response decode_direct_connect_code_validation_response(
    std::span<const std::uint8_t> payload)
{
    packet_reader reader(payload);
    direct_connect_code_validation_response value;
    value.request_id = reader.read_guid();
    value.success = reader.read_byte() != 0;
    value.gateway_node_id = reader.read_string();
    value.gateway_master_connection_id = reader.read_string();
    value.target_node_kind = static_cast<master_node_kind>(reader.read_byte());
    value.target_node_id = reader.read_string();
    value.target_master_connection_id = reader.read_string();
    value.error_message = reader.read_string();
    reader.require_finished();
    return value;
}

packet_frame create_node_auth_challenge_frame(const node_auth_challenge& value)
{
    return make_frame(
        packet_kind::control,
        master_pid_node_auth_challenge,
        master_control_schema_version,
        encode_node_auth_challenge(value));
}

gateway_direct_handshake_result complete_gateway_direct_handshake(
    const packet_frame& hello_frame,
    const packet_frame& direct_connect_code_frame,
    direct_connect_code_validator& validator,
    const std::string& backend_connection_id)
{
    require_non_empty(backend_connection_id, "Backend connection id");
    require_control_frame(hello_frame, master_pid_node_hello, master_control_schema_version);
    require_control_frame(direct_connect_code_frame, master_pid_direct_connect_code, master_control_schema_version);

    auto hello = decode_node_hello(hello_frame.payload);
    if (hello.node_kind != master_node_kind::gateway) {
        throw packet_error("Backend direct handshake only accepts Gateway nodes.");
    }

    if (hello.protocol_version != master_control_schema_version) {
        throw packet_error("Unsupported Gateway protocol version.");
    }

    require_non_empty(hello.master_connection_id, "Gateway Master connection id");

    auto code = decode_direct_connect_code(direct_connect_code_frame.payload);
    auto validation = validator.validate(
        code.code,
        hello.node_id,
        hello.master_connection_id);
    if (!validation.success) {
        throw packet_error(validation.error_message.empty()
            ? "Direct connect code validation failed."
            : validation.error_message);
    }

    if (validation.gateway_node_id != hello.node_id ||
        validation.gateway_master_connection_id != hello.master_connection_id ||
        validation.target_node_kind != master_node_kind::backend) {
        throw packet_error("Direct connect code validation returned an unexpected connection identity.");
    }

    node_accepted accepted {
        hello.node_id,
        backend_connection_id,
    };

    return gateway_direct_handshake_result {
        hello.node_id,
        hello.master_connection_id,
        make_frame(
            packet_kind::control,
            master_pid_node_accepted,
            master_control_schema_version,
            encode_node_accepted(accepted)),
    };
}

bool is_routed_packet_kind(packet_kind kind) noexcept
{
    return kind == packet_kind::request ||
           kind == packet_kind::response ||
           kind == packet_kind::notify;
}

} // namespace cpp_backend
