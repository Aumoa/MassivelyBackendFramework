using System;
using Convert = System.Convert;
using PacketCore;

namespace MasterServer.ControlPlane;

public static class BackendPacketVerifierProgramCodec
{
    public static string EncodeBase64(BackendPacketVerifierProgram? program)
    {
        if (program == null)
        {
            return string.Empty;
        }

        return Convert.ToBase64String(Encode(program));
    }

    public static BackendPacketVerifierProgram? DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Decode(Convert.FromBase64String(value));
    }

    public static byte[] Encode(BackendPacketVerifierProgram program)
    {
        if (program == null)
        {
            throw new ArgumentNullException(nameof(program));
        }

        var buffer = new byte[GetProgramSize(program)];
        var writer = new PacketWriter(buffer);
        WriteProgram(program, ref writer);
        return buffer;
    }

    public static BackendPacketVerifierProgram Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        var readState = new VerifierProgramReadState();
        var program = ReadProgram(ref reader, readState);
        if (reader.Remaining != 0)
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Verifier program has trailing bytes.");
        }

        return program;
    }

    internal static int GetNullableProgramSize(BackendPacketVerifierProgram? program)
    {
        return sizeof(byte) + (program == null ? 0 : GetProgramSize(program));
    }

    internal static void WriteNullableProgram(
        BackendPacketVerifierProgram? program,
        ref PacketWriter writer)
    {
        if (program == null)
        {
            writer.WriteByte(0);
            return;
        }

        writer.WriteByte(1);
        WriteProgram(program, ref writer);
    }

    internal static BackendPacketVerifierProgram? ReadNullableProgram(ref PacketReader reader)
    {
        return reader.ReadByte() == 0
            ? null
            : ReadProgram(ref reader, new VerifierProgramReadState());
    }

    internal static int GetProgramSize(BackendPacketVerifierProgram program)
    {
        return sizeof(byte) +
               sizeof(int) +
               sizeof(int) +
               GetInstructionsSize(program.Instructions);
    }

    internal static void WriteProgram(
        BackendPacketVerifierProgram program,
        ref PacketWriter writer)
    {
        writer.WriteByte((byte)program.TrailingBytePolicy);
        writer.WriteInt32(program.MaxTrailingByteCount);
        writer.WriteInt32(program.InstructionBudget);
        WriteInstructions(program.Instructions, ref writer);
    }

    internal static BackendPacketVerifierProgram ReadProgram(
        ref PacketReader reader,
        VerifierProgramReadState readState)
    {
        var trailingBytePolicy = (BackendPacketVerifierTrailingBytePolicy)reader.ReadByte();
        var maxTrailingByteCount = reader.ReadInt32();
        var instructionBudget = reader.ReadInt32();
        var instructions = ReadInstructions(
            ref reader,
            readState,
            depth: 0,
            allowEmpty: true);
        return new BackendPacketVerifierProgram(
            instructions,
            trailingBytePolicy,
            maxTrailingByteCount,
            instructionBudget);
    }

    private static int GetInstructionsSize(BackendPacketVerifierInstruction[] instructions)
    {
        var size = sizeof(int);
        foreach (var instruction in instructions)
        {
            size += GetInstructionSize(instruction);
        }

        return size;
    }

    private static void WriteInstructions(
        BackendPacketVerifierInstruction[] instructions,
        ref PacketWriter writer)
    {
        writer.WriteInt32(instructions.Length);
        foreach (var instruction in instructions)
        {
            WriteInstruction(instruction, ref writer);
        }
    }

    private static BackendPacketVerifierInstruction[] ReadInstructions(
        ref PacketReader reader,
        VerifierProgramReadState readState,
        int depth,
        bool allowEmpty)
    {
        if (depth > BackendPacketVerifierProgram.MaxNestingDepth)
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Verifier instruction nesting is too deep.");
        }

        var instructionCount = reader.ReadInt32();
        if (instructionCount < 0 ||
            instructionCount > BackendPacketVerifierProgram.MaxInstructionCount ||
            (!allowEmpty && instructionCount == 0))
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid verifier instruction count.");
        }

        readState.AddInstructions(instructionCount);
        var instructions = new BackendPacketVerifierInstruction[instructionCount];
        for (var i = 0; i < instructions.Length; i++)
        {
            instructions[i] = ReadInstruction(ref reader, readState, depth);
        }

        return instructions;
    }

    private static int GetInstructionSize(BackendPacketVerifierInstruction instruction)
    {
        return sizeof(byte) +
               sizeof(byte) +
               sizeof(int) +
               GetValueConstraintSize(instruction.ValueConstraint) +
               GetLengthConstraintSize(instruction.LengthConstraint) +
               sizeof(int) +
               sizeof(int) +
               sizeof(int) +
               GetInstructionsSize(instruction.Body) +
               sizeof(int) +
               sizeof(long);
    }

    private static void WriteInstruction(
        BackendPacketVerifierInstruction instruction,
        ref PacketWriter writer)
    {
        writer.WriteByte((byte)instruction.Operation);
        writer.WriteByte((byte)instruction.Primitive);
        writer.WriteInt32(instruction.TargetSlot);
        WriteValueConstraint(instruction.ValueConstraint, ref writer);
        WriteLengthConstraint(instruction.LengthConstraint, ref writer);
        writer.WriteInt32(instruction.RepeatCountSlot);
        writer.WriteInt32(instruction.MinimumRepeatCount);
        writer.WriteInt32(instruction.MaximumRepeatCount);
        WriteInstructions(instruction.Body, ref writer);
        writer.WriteInt32(instruction.BreakValueSlot);
        writer.WriteInt64(instruction.BreakValue);
    }

    private static BackendPacketVerifierInstruction ReadInstruction(
        ref PacketReader reader,
        VerifierProgramReadState readState,
        int depth)
    {
        var operation = (BackendPacketVerifierOperation)reader.ReadByte();
        var primitive = (BackendPacketVerifierPrimitive)reader.ReadByte();
        var targetSlot = reader.ReadInt32();
        var valueConstraint = ReadValueConstraint(ref reader);
        var lengthConstraint = ReadLengthConstraint(ref reader);
        var repeatCountSlot = reader.ReadInt32();
        var minimumRepeatCount = reader.ReadInt32();
        var maximumRepeatCount = reader.ReadInt32();
        var body = ReadInstructionBody(
            ref reader,
            readState,
            operation,
            depth);
        var breakValueSlot = reader.ReadInt32();
        var breakValue = reader.ReadInt64();
        return new BackendPacketVerifierInstruction(
            operation,
            primitive,
            targetSlot,
            valueConstraint,
            lengthConstraint,
            repeatCountSlot,
            minimumRepeatCount,
            maximumRepeatCount,
            body,
            breakValueSlot,
            breakValue);
    }

    private static BackendPacketVerifierInstruction[] ReadInstructionBody(
        ref PacketReader reader,
        VerifierProgramReadState readState,
        BackendPacketVerifierOperation operation,
        int depth)
    {
        var isRepeat = operation == BackendPacketVerifierOperation.Repeat;
        if (!isRepeat)
        {
            var bodyInstructionCount = reader.ReadInt32();
            if (bodyInstructionCount != 0)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Only repeat verifier instructions can contain a body.");
            }

            return Array.Empty<BackendPacketVerifierInstruction>();
        }

        return ReadInstructions(
            ref reader,
            readState,
            depth + 1,
            allowEmpty: false);
    }

    private static int GetValueConstraintSize(BackendPacketVerifierValueConstraint? constraint)
    {
        return sizeof(byte) +
               (constraint == null
                   ? 0
                   : sizeof(byte) +
                     (constraint.MinimumValue.HasValue ? sizeof(long) : 0) +
                     sizeof(byte) +
                     (constraint.MaximumValue.HasValue ? sizeof(long) : 0) +
                     sizeof(byte) +
                     (constraint.FlagsMask.HasValue ? sizeof(long) : 0) +
                     sizeof(int) +
                     (constraint.AllowedValues.Length * sizeof(long)));
    }

    private static void WriteValueConstraint(
        BackendPacketVerifierValueConstraint? constraint,
        ref PacketWriter writer)
    {
        if (constraint == null)
        {
            writer.WriteByte(0);
            return;
        }

        writer.WriteByte(1);
        writer.WriteByte(constraint.MinimumValue.HasValue ? (byte)1 : (byte)0);
        if (constraint.MinimumValue.HasValue)
        {
            writer.WriteInt64(constraint.MinimumValue.Value);
        }

        writer.WriteByte(constraint.MaximumValue.HasValue ? (byte)1 : (byte)0);
        if (constraint.MaximumValue.HasValue)
        {
            writer.WriteInt64(constraint.MaximumValue.Value);
        }

        writer.WriteByte(constraint.FlagsMask.HasValue ? (byte)1 : (byte)0);
        if (constraint.FlagsMask.HasValue)
        {
            writer.WriteInt64(constraint.FlagsMask.Value);
        }

        writer.WriteInt32(constraint.AllowedValues.Length);
        foreach (var value in constraint.AllowedValues)
        {
            writer.WriteInt64(value);
        }
    }

    private static BackendPacketVerifierValueConstraint? ReadValueConstraint(ref PacketReader reader)
    {
        if (reader.ReadByte() == 0)
        {
            return null;
        }

        long? minimumValue = reader.ReadByte() == 0 ? null : reader.ReadInt64();
        long? maximumValue = reader.ReadByte() == 0 ? null : reader.ReadInt64();
        long? flagsMask = reader.ReadByte() == 0 ? null : reader.ReadInt64();
        var allowedValueCount = reader.ReadInt32();
        if (allowedValueCount < 0 || allowedValueCount > BackendPacketVerifierProgram.MaxInstructionCount)
        {
            throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Invalid verifier enum value count.");
        }

        var allowedValues = new long[allowedValueCount];
        for (var i = 0; i < allowedValues.Length; i++)
        {
            allowedValues[i] = reader.ReadInt64();
        }

        return new BackendPacketVerifierValueConstraint(
            minimumValue,
            maximumValue,
            allowedValues,
            flagsMask);
    }

    private static int GetLengthConstraintSize(BackendPacketVerifierLengthConstraint? constraint)
    {
        return sizeof(byte) + (constraint == null ? 0 : sizeof(int) * 4);
    }

    private static void WriteLengthConstraint(
        BackendPacketVerifierLengthConstraint? constraint,
        ref PacketWriter writer)
    {
        if (constraint == null)
        {
            writer.WriteByte(0);
            return;
        }

        writer.WriteByte(1);
        writer.WriteInt32(constraint.FixedLength);
        writer.WriteInt32(constraint.SourceSlot);
        writer.WriteInt32(constraint.MinimumLength);
        writer.WriteInt32(constraint.MaximumLength);
    }

    private static BackendPacketVerifierLengthConstraint? ReadLengthConstraint(ref PacketReader reader)
    {
        if (reader.ReadByte() == 0)
        {
            return null;
        }

        var fixedLength = reader.ReadInt32();
        var sourceSlot = reader.ReadInt32();
        var minimumLength = reader.ReadInt32();
        var maximumLength = reader.ReadInt32();
        return fixedLength >= 0
            ? BackendPacketVerifierLengthConstraint.Fixed(fixedLength)
            : BackendPacketVerifierLengthConstraint.Dynamic(sourceSlot, minimumLength, maximumLength);
    }

    internal sealed class VerifierProgramReadState
    {
        private int m_InstructionCount;

        public void AddInstructions(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            m_InstructionCount += count;
            if (m_InstructionCount > BackendPacketVerifierProgram.MaxInstructionCount)
            {
                throw new PacketFormatException(PacketValidationError.InvalidStringLength, "Verifier program contains too many instructions.");
            }
        }
    }
}
