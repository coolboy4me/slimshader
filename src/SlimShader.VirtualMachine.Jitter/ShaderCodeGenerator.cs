﻿using System;
using System.Linq;
using System.Text;
using SlimShader.Chunks.Common;
using SlimShader.Chunks.Shex;
using SlimShader.Chunks.Shex.Tokens;
using SlimShader.VirtualMachine.Analysis.ExecutableInstructions;
using SlimShader.VirtualMachine.Util;

namespace SlimShader.VirtualMachine.Jitter
{
    internal static class ShaderCodeGenerator
    {
        public static string Generate(ExecutableInstruction[] instructions)
        {
            var instructionsCode = new StringBuilder();
            foreach (var instruction in instructions)
            {
                instructionsCode.AppendLineIndent(2, "// " + instruction);
                GenerateInstructionCode(instructionsCode, instruction);
                instructionsCode.AppendLine();
            }

            return @"
using System.Collections.Generic;
using SlimShader;
using SlimShader.VirtualMachine;
using SlimShader.VirtualMachine.Analysis.ExecutableInstructions;
using SlimShader.VirtualMachine.Execution;

public static class DynamicShaderExecutor
{
    public static IEnumerable<ExecutionResponse> Execute(
        VirtualMachine virtualMachine, ExecutionContext[] executionContexts,
        ExecutableInstruction[] instructions)
    {
        var warp = new Warp(executionContexts.Length);
        var activeExecutionContexts = Warp.GetActiveExecutionContexts(executionContexts, warp.DivergenceStack.Peek());

" + instructionsCode + @"
    }
}";
        }

        private static void GenerateInstructionCode(StringBuilder sb, ExecutableInstruction instruction)
        {
            switch (instruction.OpcodeType)
            {
                case Execution.ExecutableOpcodeType.Mad:
                    GenerateExecute3(sb, instruction, "Mad");
                    break;
                case Execution.ExecutableOpcodeType.Mul:
                    GenerateExecute2(sb, instruction, "Mul");
                    break;
                case Execution.ExecutableOpcodeType.Mov :
                    GenerateExecute1(sb, instruction, "Mov");
                    break;
                case Execution.ExecutableOpcodeType.Ret :
                    sb.AppendLineIndent(2, "yield return ExecutionResponse.Finished;");
                    break;
                default :
                    throw new InvalidOperationException(instruction.OpcodeType + " is not yet supported.");
            }
        }

        private static void GenerateExecute1(StringBuilder sb, ExecutableInstruction instruction, string methodName)
        {
            sb.AppendLineIndent(2, "foreach (var context in activeExecutionContexts)");
            sb.AppendLineIndent(2, "{");
            sb.AppendLineIndent(2, "    var src0 = {0};", GenerateGetOperandValue(instruction.Operands[1], NumberType.Float));
            sb.AppendLineIndent(2, "    var result = InstructionImplementations.{0}({1}, ref src0);",
                methodName, instruction.Saturate.ToString().ToLower());
            GenerateSetRegisterValue(sb, instruction.Operands[0]);
            sb.AppendLineIndent(2, "}");
        }

        private static void GenerateExecute2(StringBuilder sb, ExecutableInstruction instruction, string methodName)
        {
            sb.AppendLineIndent(2, "foreach (var context in activeExecutionContexts)");
            sb.AppendLineIndent(2, "{");
            sb.AppendLineIndent(2, "    var src0 = {0};", GenerateGetOperandValue(instruction.Operands[1], NumberType.Float));
            sb.AppendLineIndent(2, "    var src1 = {0};", GenerateGetOperandValue(instruction.Operands[2], NumberType.Float));
            sb.AppendLineIndent(2, "    var result = InstructionImplementations.{0}({1}, ref src0, ref src1);",
                methodName, instruction.Saturate.ToString().ToLower());
            GenerateSetRegisterValue(sb, instruction.Operands[0]);
            sb.AppendLineIndent(2, "}");
        }

        private static void GenerateExecute3(StringBuilder sb, ExecutableInstruction instruction, string methodName)
        {
            sb.AppendLineIndent(2, "foreach (var context in activeExecutionContexts)");
            sb.AppendLineIndent(2, "{");
            sb.AppendLineIndent(2, "    var src0 = {0};", GenerateGetOperandValue(instruction.Operands[1], NumberType.Float));
            sb.AppendLineIndent(2, "    var src1 = {0};", GenerateGetOperandValue(instruction.Operands[2], NumberType.Float));
            sb.AppendLineIndent(2, "    var src2 = {0};", GenerateGetOperandValue(instruction.Operands[3], NumberType.Float));
            sb.AppendLineIndent(2, "    var result = InstructionImplementations.{0}({1}, ref src0, ref src1, ref src2);",
                methodName, instruction.Saturate.ToString().ToLower());
            GenerateSetRegisterValue(sb, instruction.Operands[0]);
            sb.AppendLineIndent(2, "}");
        }

        private static void GenerateSetRegisterValue(StringBuilder sb, Operand operand)
        {
            var register = GetRegister(operand);

            if (operand.ComponentMask.HasFlag(ComponentMask.X)
                && operand.ComponentMask.HasFlag(ComponentMask.Y)
                && operand.ComponentMask.HasFlag(ComponentMask.Z)
                && operand.ComponentMask.HasFlag(ComponentMask.W))
            {
                sb.AppendLineIndent(2, "    {0} = result;", register);
                return;
            }

            if (operand.ComponentMask.HasFlag(ComponentMask.X))
                sb.AppendLineIndent(2, "    {0}.Number0 = result.Number0;", register);
            if (operand.ComponentMask.HasFlag(ComponentMask.Y))
                sb.AppendLineIndent(2, "    {0}.Number1 = result.Number1;", register);
            if (operand.ComponentMask.HasFlag(ComponentMask.Z))
                sb.AppendLineIndent(2, "    {0}.Number2 = result.Number2;", register);
            if (operand.ComponentMask.HasFlag(ComponentMask.W))
                sb.AppendLineIndent(2, "    {0}.Number3 = result.Number3;", register);
        }

        private static string GetRegister(Operand operand)
        {
            return string.Format("context.{0}{1}",
                GetRegisterName(operand.OperandType),
                GetRegisterIndex(operand));
        }

        private static string GetRegisterIndex(Operand operand)
        {
            switch (operand.OperandType)
            {
                case OperandType.Output:
                case OperandType.Temp:
                    return string.Format("[{0}]",
                        GetRegisterIndex(operand.Indices[0]));
                case OperandType.Input:
                    return string.Format("[0][{0}]",
                        GetRegisterIndex(operand.Indices[0]));
                case OperandType.ConstantBuffer:
                case OperandType.IndexableTemp :
                    return string.Format("[{0}][{1}]",
                        GetRegisterIndex(operand.Indices[0]),
                        GetRegisterIndex(operand.Indices[1]));
                default :
                    throw new ArgumentException("Unsupported operand type: " + operand.OperandType);
            }
        }

        private static string GetRegisterIndex(OperandIndex index)
        {
            string result = index.Value.ToString();
            switch (index.Representation)
            {
                case OperandIndexRepresentation.Immediate32PlusRelative:
                case OperandIndexRepresentation.Immediate64PlusRelative:
                case OperandIndexRepresentation.Relative:
                    throw new NotImplementedException();
            }
            return result;
        }

        private static string GetRegisterName(OperandType operandType)
        {
            switch (operandType)
            {
                case OperandType.Temp:
                case OperandType.Input:
                case OperandType.Output:
                case OperandType.IndexableTemp:
                case OperandType.ConstantBuffer:
                    return operandType.ToString() + "s";
                default:
                    throw new ArgumentOutOfRangeException("operandType");
            }
        }

        private static string GenerateGetOperandValue(Operand operand, NumberType numberType)
        {
            switch (operand.OperandType)
            {
                case OperandType.Immediate32 :
                case OperandType.Immediate64:
                    var value = OperandUtility.ApplyOperandModifier(operand.ImmediateValues, numberType, operand.Modifier);
                    return string.Format("new Number4({0}f, {1}f, {2}f, {3}f)",
                        value.Float0, value.Float1, value.Float2, value.Float3);
                case OperandType.ConstantBuffer:
                case OperandType.IndexableTemp:
                case OperandType.Input:
                case OperandType.Temp:
                    // TODO: Apply modifier and selection mode.
                    return ApplyOperandSelectionMode(GetRegister(operand), operand);
                default:
                    throw new ArgumentException("Unsupported operand type: " + operand.OperandType);
            }
        }

        private static string ApplyOperandSelectionMode(string register, Operand operand)
        {
            if (operand.SelectionMode != Operand4ComponentSelectionMode.Swizzle)
                return register;

            if (operand.Swizzles[0] == Operand4ComponentName.X
                && operand.Swizzles[1] == Operand4ComponentName.Y
                && operand.Swizzles[2] == Operand4ComponentName.Z
                && operand.Swizzles[3] == Operand4ComponentName.W)
                return register;

            return string.Format("{0}.{1}{2}{3}{4}", register,
                GetOperand4ComponentName(operand.Swizzles[0]),
                GetOperand4ComponentName(operand.Swizzles[1]).ToLower(),
                GetOperand4ComponentName(operand.Swizzles[2]).ToLower(),
                GetOperand4ComponentName(operand.Swizzles[3]).ToLower());
        }

        private static string GetOperand4ComponentName(Operand4ComponentName name)
        {
            switch (name)
            {
                case Operand4ComponentName.X :
                    return "X";
                case Operand4ComponentName.Y:
                    return "Y";
                case Operand4ComponentName.Z:
                    return "Z";
                case Operand4ComponentName.W:
                    return "W";
                default :
                    throw new NotImplementedException();
            }
        }

        private static void AppendLineIndent(this StringBuilder sb, int indent, string line)
        {
            sb.Append(new string(Enumerable.Repeat(' ', indent * 4).ToArray()));
            sb.AppendLine(line);
        }

        private static void AppendLineIndent(this StringBuilder sb, int indent, string format, params object[] args)
        {
            sb.Append(new string(Enumerable.Repeat(' ', indent * 4).ToArray()));
            sb.AppendLine(string.Format(format, args));
        }
    }
}