using System;
using System.Buffers;
using System.Text;
using PacketCore;

namespace MasterServer.ControlPlane;

public enum BackendPacketVerifierTrailingBytePolicy : byte
{
    RequireEnd = 1,
    AllowTrailingBytes = 2
}

public sealed class BackendPacketVerifierProgram
{
    public const int MaxInstructionCount = 256;
    public const int MaxInstructionBudget = 4096;
    public const int DefaultInstructionBudget = 512;
    public const int MaxLoopRepeatCount = 1024;
    public const int MaxNestingDepth = 8;
    private static readonly UTF8Encoding s_StrictUtf8 = new(false, true);

    public BackendPacketVerifierProgram(
        BackendPacketVerifierInstruction[] instructions,
        BackendPacketVerifierTrailingBytePolicy trailingBytePolicy = BackendPacketVerifierTrailingBytePolicy.RequireEnd,
        int maxTrailingByteCount = 0,
        int instructionBudget = DefaultInstructionBudget)
    {
        Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
        TrailingBytePolicy = trailingBytePolicy;
        MaxTrailingByteCount = maxTrailingByteCount;
        InstructionBudget = instructionBudget;
        Validate();
    }

    public BackendPacketVerifierInstruction[] Instructions { get; }

    public BackendPacketVerifierTrailingBytePolicy TrailingBytePolicy { get; }

    public int MaxTrailingByteCount { get; }

    public int InstructionBudget { get; }

    public void Validate()
    {
        if (!Enum.IsDefined(typeof(BackendPacketVerifierTrailingBytePolicy), TrailingBytePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(TrailingBytePolicy));
        }

        if (MaxTrailingByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTrailingByteCount));
        }

        if (TrailingBytePolicy == BackendPacketVerifierTrailingBytePolicy.RequireEnd &&
            MaxTrailingByteCount != 0)
        {
            throw new ArgumentException("RequireEnd verifier programs cannot allow trailing bytes.", nameof(MaxTrailingByteCount));
        }

        if (InstructionBudget <= 0 || InstructionBudget > MaxInstructionBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(InstructionBudget));
        }

        var assignedSlots = new bool[BackendPacketVerifierInstruction.MaxSlotCount];
        var instructionCount = ValidateInstructions(
            Instructions,
            assignedSlots,
            insideLoop: false,
            depth: 0);
        if (instructionCount > MaxInstructionCount)
        {
            throw new ArgumentException($"Verifier programs can contain at most {MaxInstructionCount} instructions.", nameof(Instructions));
        }
    }

    public BackendPacketVerifierResult Verify(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        var slots = ArrayPool<long>.Shared.Rent(BackendPacketVerifierInstruction.MaxSlotCount);
        var assignedSlots = ArrayPool<bool>.Shared.Rent(BackendPacketVerifierInstruction.MaxSlotCount);
        try
        {
            Array.Clear(slots, 0, BackendPacketVerifierInstruction.MaxSlotCount);
            Array.Clear(assignedSlots, 0, BackendPacketVerifierInstruction.MaxSlotCount);
            var budget = InstructionBudget;
            var executionResult = ExecuteInstructions(
                Instructions,
                ref reader,
                slots,
                assignedSlots,
                ref budget,
                insideLoop: false);
            if (executionResult.Failure != BackendPacketManifestValidationFailure.None)
            {
                return BackendPacketVerifierResult.Rejected(executionResult.Failure);
            }

            if (TrailingBytePolicy == BackendPacketVerifierTrailingBytePolicy.RequireEnd &&
                reader.Remaining != 0)
            {
                return BackendPacketVerifierResult.Rejected(BackendPacketManifestValidationFailure.VerifierTrailingBytes);
            }

            if (TrailingBytePolicy == BackendPacketVerifierTrailingBytePolicy.AllowTrailingBytes &&
                reader.Remaining > MaxTrailingByteCount)
            {
                return BackendPacketVerifierResult.Rejected(BackendPacketManifestValidationFailure.VerifierTrailingBytes);
            }

            return BackendPacketVerifierResult.Accepted;
        }
        finally
        {
            ArrayPool<long>.Shared.Return(slots, clearArray: true);
            ArrayPool<bool>.Shared.Return(assignedSlots, clearArray: true);
        }
    }

    private static int ValidateInstructions(
        BackendPacketVerifierInstruction[] instructions,
        bool[] assignedSlots,
        bool insideLoop,
        int depth)
    {
        if (instructions == null)
        {
            throw new ArgumentNullException(nameof(instructions));
        }

        if (depth > MaxNestingDepth)
        {
            throw new ArgumentException($"Verifier programs can nest at most {MaxNestingDepth} repeat bodies.", nameof(instructions));
        }

        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction == null)
            {
                throw new ArgumentException("Verifier instructions cannot contain null entries.", nameof(instructions));
            }

            count++;
            switch (instruction.Operation)
            {
                case BackendPacketVerifierOperation.ReadPrimitive:
                    if (instruction.TargetSlot != BackendPacketVerifierInstruction.NoSlot)
                    {
                        assignedSlots[instruction.TargetSlot] = true;
                    }
                    break;

                case BackendPacketVerifierOperation.ReadBytes:
                case BackendPacketVerifierOperation.ReadUtf8String:
                    ValidateLengthConstraint(instruction.LengthConstraint!, assignedSlots);
                    if (instruction.TargetSlot != BackendPacketVerifierInstruction.NoSlot)
                    {
                        assignedSlots[instruction.TargetSlot] = true;
                    }
                    break;

                case BackendPacketVerifierOperation.Repeat:
                    RequireAssignedSlot(instruction.RepeatCountSlot, assignedSlots);
                    if (instruction.MaximumRepeatCount > MaxLoopRepeatCount)
                    {
                        throw new ArgumentOutOfRangeException(nameof(instruction.MaximumRepeatCount));
                    }

                    var bodyAssignedSlots = (bool[])assignedSlots.Clone();
                    count += ValidateInstructions(
                        instruction.Body,
                        bodyAssignedSlots,
                        insideLoop: true,
                        depth + 1);
                    break;

                case BackendPacketVerifierOperation.BreakRepeatIfValueEquals:
                    if (!insideLoop)
                    {
                        throw new ArgumentException("Break verifier instructions must be inside a repeat body.", nameof(instructions));
                    }

                    RequireAssignedSlot(instruction.BreakValueSlot, assignedSlots);
                    break;
            }
        }

        return count;
    }

    private static void ValidateLengthConstraint(
        BackendPacketVerifierLengthConstraint lengthConstraint,
        bool[] assignedSlots)
    {
        if (lengthConstraint.IsDynamic)
        {
            RequireAssignedSlot(lengthConstraint.SourceSlot, assignedSlots);
        }
    }

    private static void RequireAssignedSlot(
        int slot,
        bool[] assignedSlots)
    {
        if (!BackendPacketVerifierInstruction.IsValidSlot(slot) ||
            !assignedSlots[slot])
        {
            throw new ArgumentException($"Verifier slot {slot} is used before it is read.");
        }
    }

    private static BackendPacketVerifierExecutionResult ExecuteInstructions(
        BackendPacketVerifierInstruction[] instructions,
        ref PacketReader reader,
        long[] slots,
        bool[] assignedSlots,
        ref int budget,
        bool insideLoop)
    {
        foreach (var instruction in instructions)
        {
            budget--;
            if (budget < 0)
            {
                return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierInstructionBudgetExceeded);
            }

            var result = ExecuteInstruction(
                instruction,
                ref reader,
                slots,
                assignedSlots,
                ref budget,
                insideLoop);
            if (result.Failure != BackendPacketManifestValidationFailure.None ||
                result.BreakLoop)
            {
                return result;
            }
        }

        return BackendPacketVerifierExecutionResult.Accepted;
    }

    private static BackendPacketVerifierExecutionResult ExecuteInstruction(
        BackendPacketVerifierInstruction instruction,
        ref PacketReader reader,
        long[] slots,
        bool[] assignedSlots,
        ref int budget,
        bool insideLoop)
    {
        switch (instruction.Operation)
        {
            case BackendPacketVerifierOperation.ReadPrimitive:
                return ReadPrimitive(instruction, ref reader, slots, assignedSlots);

            case BackendPacketVerifierOperation.ReadBytes:
                return ReadBytes(instruction, ref reader, slots, assignedSlots, validateUtf8: false);

            case BackendPacketVerifierOperation.ReadUtf8String:
                return ReadBytes(instruction, ref reader, slots, assignedSlots, validateUtf8: true);

            case BackendPacketVerifierOperation.Repeat:
                return ExecuteRepeat(instruction, ref reader, slots, assignedSlots, ref budget);

            case BackendPacketVerifierOperation.BreakRepeatIfValueEquals:
                return insideLoop && slots[instruction.BreakValueSlot] == instruction.BreakValue
                    ? BackendPacketVerifierExecutionResult.Break
                    : BackendPacketVerifierExecutionResult.Accepted;

            default:
                return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierRejected);
        }
    }

    private static BackendPacketVerifierExecutionResult ReadPrimitive(
        BackendPacketVerifierInstruction instruction,
        ref PacketReader reader,
        long[] slots,
        bool[] assignedSlots)
    {
        long value = 0;
        switch (instruction.Primitive)
        {
            case BackendPacketVerifierPrimitive.UInt8:
                if (reader.Remaining < 1)
                {
                    return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
                }

                value = reader.ReadByte();
                break;

            case BackendPacketVerifierPrimitive.UInt16:
                if (reader.Remaining < 2)
                {
                    return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
                }

                value = reader.ReadUInt16();
                break;

            case BackendPacketVerifierPrimitive.UInt32:
                if (reader.Remaining < 4)
                {
                    return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
                }

                value = reader.ReadUInt32();
                break;

            case BackendPacketVerifierPrimitive.Int32:
                if (reader.Remaining < 4)
                {
                    return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
                }

                value = reader.ReadInt32();
                break;

            case BackendPacketVerifierPrimitive.Int64:
                if (reader.Remaining < 8)
                {
                    return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
                }

                value = reader.ReadInt64();
                break;

            case BackendPacketVerifierPrimitive.Guid:
                if (reader.Remaining < 16)
                {
                    return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
                }

                reader.ReadGuid();
                return BackendPacketVerifierExecutionResult.Accepted;
        }

        var constraintFailure = (instruction.ValueConstraint ?? BackendPacketVerifierValueConstraint.Any).Validate(value);
        if (constraintFailure != BackendPacketManifestValidationFailure.None)
        {
            return BackendPacketVerifierExecutionResult.Rejected(constraintFailure);
        }

        if (instruction.TargetSlot != BackendPacketVerifierInstruction.NoSlot)
        {
            slots[instruction.TargetSlot] = value;
            assignedSlots[instruction.TargetSlot] = true;
        }

        return BackendPacketVerifierExecutionResult.Accepted;
    }

    private static BackendPacketVerifierExecutionResult ReadBytes(
        BackendPacketVerifierInstruction instruction,
        ref PacketReader reader,
        long[] slots,
        bool[] assignedSlots,
        bool validateUtf8)
    {
        var lengthFailure = instruction.LengthConstraint!.Resolve(slots, out var length);
        if (lengthFailure != BackendPacketManifestValidationFailure.None)
        {
            return BackendPacketVerifierExecutionResult.Rejected(lengthFailure);
        }

        if (reader.Remaining < length)
        {
            return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierPayloadTruncated);
        }

        var bytes = reader.ReadBytes(length);
        if (validateUtf8)
        {
            try
            {
                s_StrictUtf8.GetCharCount(bytes);
            }
            catch (DecoderFallbackException)
            {
                return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierInvalidUtf8);
            }
        }

        if (instruction.TargetSlot != BackendPacketVerifierInstruction.NoSlot)
        {
            slots[instruction.TargetSlot] = length;
            assignedSlots[instruction.TargetSlot] = true;
        }

        return BackendPacketVerifierExecutionResult.Accepted;
    }

    private static BackendPacketVerifierExecutionResult ExecuteRepeat(
        BackendPacketVerifierInstruction instruction,
        ref PacketReader reader,
        long[] slots,
        bool[] assignedSlots,
        ref int budget)
    {
        var repeatCount = slots[instruction.RepeatCountSlot];
        if (repeatCount < instruction.MinimumRepeatCount ||
            repeatCount > instruction.MaximumRepeatCount)
        {
            return BackendPacketVerifierExecutionResult.Rejected(BackendPacketManifestValidationFailure.VerifierRepeatCountOutOfRange);
        }

        for (var i = 0; i < repeatCount; i++)
        {
            var result = ExecuteInstructions(
                instruction.Body,
                ref reader,
                slots,
                assignedSlots,
                ref budget,
                insideLoop: true);
            if (result.Failure != BackendPacketManifestValidationFailure.None)
            {
                return result;
            }

            if (result.BreakLoop)
            {
                break;
            }
        }

        return BackendPacketVerifierExecutionResult.Accepted;
    }

    private readonly struct BackendPacketVerifierExecutionResult
    {
        private BackendPacketVerifierExecutionResult(
            BackendPacketManifestValidationFailure failure,
            bool breakLoop)
        {
            Failure = failure;
            BreakLoop = breakLoop;
        }

        public BackendPacketManifestValidationFailure Failure { get; }

        public bool BreakLoop { get; }

        public static BackendPacketVerifierExecutionResult Accepted { get; } = new(
            BackendPacketManifestValidationFailure.None,
            breakLoop: false);

        public static BackendPacketVerifierExecutionResult Break { get; } = new(
            BackendPacketManifestValidationFailure.None,
            breakLoop: true);

        public static BackendPacketVerifierExecutionResult Rejected(BackendPacketManifestValidationFailure failure)
        {
            return new BackendPacketVerifierExecutionResult(failure, breakLoop: false);
        }
    }
}
