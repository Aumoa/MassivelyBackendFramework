using System.Globalization;
using System.Text.Json;
using AI;

namespace AI.Tools;

public sealed class CalculationTools
{
    private const int MaxExpressionLength = 1000;

    [ToolFunction(
        Name = "calculate",
        Description = """
        수학 계산을 정확하게 수행합니다. 사용자가 산술, 비율, 퍼센트, 거듭제곱, 제곱근, 로그, 삼각함수, 반올림, 합계/평균, 조합/순열 등 계산이 필요한 질문을 하면 답변 전에 이 도구로 검산하세요.
        지원 예: 1 + 2 * 3, (10 + 5) / 3, 2^10, sqrt(2), pow(2, 8), log(100, 10), sin(pi / 2), sind(30), round(10 / 3, 2), sum(1, 2, 3), avg(1, 2, 3), min(1, 2), max(1, 2), 50%, choose(5, 2).
        각도 단위 삼각함수는 기본적으로 라디안입니다. 도 단위가 필요하면 sind/cosd/tand/asind/acosd/atand 또는 deg/rad 함수를 사용하세요. 모듈로는 mod(a, b)를 사용하세요.
        """)]
    public string Calculate(
        [ToolParameterInfo(Description = "계산할 수식입니다. 명시적인 연산자와 함수 형태를 사용하세요. 예: \"round((12500 * 0.15) + 320, 2)\"")]
        string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return SerializeError(expression, "empty_expression", "계산할 수식이 비어 있습니다.");
        }

        if (expression.Length > MaxExpressionLength)
        {
            return SerializeError(expression, "expression_too_long", $"수식은 {MaxExpressionLength}자 이하로 입력해야 합니다.");
        }

        try
        {
            var parser = new ExpressionParser(expression);
            var result = parser.Parse();

            if (!double.IsFinite(result))
            {
                return SerializeError(expression, "non_finite_result", "계산 결과가 유한한 숫자가 아닙니다.");
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                expression,
                result = FormatNumber(result)
            });
        }
        catch (CalculationException e)
        {
            return SerializeError(expression, e.Code, e.Message);
        }
        catch (Exception e)
        {
            return SerializeError(expression, "calculation_failed", e.Message);
        }
    }

    private static string SerializeError(string? expression, string code, string message)
    {
        return JsonSerializer.Serialize(new
        {
            status = "error",
            expression,
            code,
            message
        });
    }

    private static string FormatNumber(double value)
    {
        if (Math.Abs(value - Math.Round(value)) <= Math.Max(1, Math.Abs(value)) * 1e-12)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private sealed class ExpressionParser(string expression)
    {
        private const int MaxDepth = 64;
        private const int MaxOperations = 4096;

        private readonly string m_Expression = expression;
        private int m_Index;
        private int m_Operations;

        public double Parse()
        {
            var value = ParseExpression(0);
            SkipWhitespace();

            if (!IsAtEnd)
            {
                throw Error("unexpected_token", $"예상하지 못한 토큰입니다: '{Current}'");
            }

            return value;
        }

        private bool IsAtEnd => m_Index >= m_Expression.Length;

        private char Current => IsAtEnd ? '\0' : m_Expression[m_Index];

        private double ParseExpression(int depth)
        {
            GuardDepth(depth);
            return ParseAdditive(depth + 1);
        }

        private double ParseAdditive(int depth)
        {
            var value = ParseMultiplicative(depth + 1);

            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    CountOperation();
                    value += ParseMultiplicative(depth + 1);
                }
                else if (Match('-'))
                {
                    CountOperation();
                    value -= ParseMultiplicative(depth + 1);
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseMultiplicative(int depth)
        {
            var value = ParseUnary(depth + 1);

            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    CountOperation();
                    value *= ParseUnary(depth + 1);
                }
                else if (Match('/'))
                {
                    CountOperation();
                    var divisor = ParseUnary(depth + 1);
                    if (divisor == 0)
                    {
                        throw Error("division_by_zero", "0으로 나눌 수 없습니다.");
                    }

                    value /= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseUnary(int depth)
        {
            SkipWhitespace();

            if (Match('+'))
            {
                CountOperation();
                return ParseUnary(depth + 1);
            }

            if (Match('-'))
            {
                CountOperation();
                return -ParseUnary(depth + 1);
            }

            return ParsePower(depth + 1);
        }

        private double ParsePower(int depth)
        {
            var value = ParsePostfix(depth + 1);
            SkipWhitespace();

            if (Match('^'))
            {
                CountOperation();
                value = Math.Pow(value, ParseUnary(depth + 1));
            }

            return value;
        }

        private double ParsePostfix(int depth)
        {
            var value = ParsePrimary(depth + 1);

            while (true)
            {
                SkipWhitespace();

                if (Match('!'))
                {
                    CountOperation();
                    value = Factorial(value);
                }
                else if (Match('%'))
                {
                    CountOperation();
                    value /= 100;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParsePrimary(int depth)
        {
            GuardDepth(depth);
            SkipWhitespace();

            if (Match('('))
            {
                var value = ParseExpression(depth + 1);
                SkipWhitespace();
                Expect(')', "missing_closing_parenthesis", "닫는 괄호 ')'가 필요합니다.");
                return value;
            }

            if (char.IsDigit(Current) || Current == '.')
            {
                return ParseNumber();
            }

            if (IsIdentifierStart(Current))
            {
                return ParseIdentifierOrFunction(depth + 1);
            }

            throw Error("expected_value", "숫자, 함수, 상수 또는 여는 괄호가 필요합니다.");
        }

        private double ParseNumber()
        {
            var start = m_Index;

            while (char.IsDigit(Current))
            {
                m_Index++;
            }

            if (Current == '.')
            {
                m_Index++;
                while (char.IsDigit(Current))
                {
                    m_Index++;
                }
            }

            if (Current is 'e' or 'E')
            {
                var exponentStart = m_Index;
                m_Index++;

                if (Current is '+' or '-')
                {
                    m_Index++;
                }

                if (!char.IsDigit(Current))
                {
                    m_Index = exponentStart;
                }
                else
                {
                    while (char.IsDigit(Current))
                    {
                        m_Index++;
                    }
                }
            }

            var token = m_Expression[start..m_Index];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw Error("invalid_number", $"숫자를 해석할 수 없습니다: {token}");
            }

            return value;
        }

        private double ParseIdentifierOrFunction(int depth)
        {
            var name = ParseIdentifier();
            SkipWhitespace();

            if (!Match('('))
            {
                return GetConstant(name);
            }

            var args = new List<double>();
            SkipWhitespace();

            if (!Match(')'))
            {
                while (true)
                {
                    args.Add(ParseExpression(depth + 1));
                    SkipWhitespace();

                    if (Match(','))
                    {
                        continue;
                    }

                    Expect(')', "missing_closing_parenthesis", "함수 인자 목록을 닫는 ')'가 필요합니다.");
                    break;
                }
            }

            CountOperation();
            return InvokeFunction(name, args);
        }

        private string ParseIdentifier()
        {
            var start = m_Index;
            m_Index++;

            while (IsIdentifierPart(Current))
            {
                m_Index++;
            }

            return m_Expression[start..m_Index];
        }

        private double GetConstant(string name)
        {
            return name.ToLowerInvariant() switch
            {
                "pi" => Math.PI,
                "e" => Math.E,
                "tau" => Math.Tau,
                "phi" => 1.618033988749895,
                _ => throw Error("unknown_identifier", $"알 수 없는 상수 또는 함수입니다: {name}")
            };
        }

        private double InvokeFunction(string name, List<double> args)
        {
            var normalizedName = name.ToLowerInvariant();

            return normalizedName switch
            {
                "abs" => OneArg(name, args, Math.Abs),
                "sqrt" => OneArg(name, args, x => RequireDomain(x >= 0, name, "0 이상의 값이 필요합니다.", Math.Sqrt(x))),
                "cbrt" => OneArg(name, args, Math.Cbrt),
                "floor" => OneArg(name, args, Math.Floor),
                "ceil" or "ceiling" => OneArg(name, args, Math.Ceiling),
                "trunc" or "truncate" => OneArg(name, args, Math.Truncate),
                "sign" => OneArg(name, args, x => Math.Sign(x)),
                "exp" => OneArg(name, args, Math.Exp),
                "ln" => OneArg(name, args, x => RequireDomain(x > 0, name, "0보다 큰 값이 필요합니다.", Math.Log(x))),
                "log10" => OneArg(name, args, x => RequireDomain(x > 0, name, "0보다 큰 값이 필요합니다.", Math.Log10(x))),
                "log2" => OneArg(name, args, x => RequireDomain(x > 0, name, "0보다 큰 값이 필요합니다.", Math.Log2(x))),
                "sin" => OneArg(name, args, Math.Sin),
                "cos" => OneArg(name, args, Math.Cos),
                "tan" => OneArg(name, args, Math.Tan),
                "asin" => OneArg(name, args, x => RequireDomain(x is >= -1 and <= 1, name, "-1 이상 1 이하의 값이 필요합니다.", Math.Asin(x))),
                "acos" => OneArg(name, args, x => RequireDomain(x is >= -1 and <= 1, name, "-1 이상 1 이하의 값이 필요합니다.", Math.Acos(x))),
                "atan" => OneArg(name, args, Math.Atan),
                "sinh" => OneArg(name, args, Math.Sinh),
                "cosh" => OneArg(name, args, Math.Cosh),
                "tanh" => OneArg(name, args, Math.Tanh),
                "deg" => OneArg(name, args, x => x * 180 / Math.PI),
                "rad" => OneArg(name, args, x => x * Math.PI / 180),
                "sind" => OneArg(name, args, x => Math.Sin(x * Math.PI / 180)),
                "cosd" => OneArg(name, args, x => Math.Cos(x * Math.PI / 180)),
                "tand" => OneArg(name, args, x => Math.Tan(x * Math.PI / 180)),
                "asind" => OneArg(name, args, x => RequireDomain(x is >= -1 and <= 1, name, "-1 이상 1 이하의 값이 필요합니다.", Math.Asin(x) * 180 / Math.PI)),
                "acosd" => OneArg(name, args, x => RequireDomain(x is >= -1 and <= 1, name, "-1 이상 1 이하의 값이 필요합니다.", Math.Acos(x) * 180 / Math.PI)),
                "atand" => OneArg(name, args, x => Math.Atan(x) * 180 / Math.PI),
                "round" => Round(name, args),
                "pow" => TwoArgs(name, args, Math.Pow),
                "root" => TwoArgs(name, args, Root),
                "log" => Log(name, args),
                "atan2" => TwoArgs(name, args, Math.Atan2),
                "min" => ManyArgs(name, args, values => values.Min()),
                "max" => ManyArgs(name, args, values => values.Max()),
                "sum" => ManyArgs(name, args, values => values.Sum()),
                "avg" or "average" => ManyArgs(name, args, values => values.Average()),
                "mod" => TwoArgs(name, args, (x, y) => RequireDomain(y != 0, name, "0으로 나머지 연산을 할 수 없습니다.", x % y)),
                "clamp" => Clamp(name, args),
                "fact" or "factorial" => OneArg(name, args, Factorial),
                "choose" or "comb" or "combination" => TwoArgs(name, args, Combination),
                "perm" or "permutation" => TwoArgs(name, args, Permutation),
                _ => throw Error("unknown_function", $"지원하지 않는 함수입니다: {name}")
            };
        }

        private double Round(string name, IReadOnlyList<double> args)
        {
            return args.Count switch
            {
                1 => Math.Round(args[0], MidpointRounding.AwayFromZero),
                2 => RoundWithDigits(name, args[0], args[1]),
                _ => throw WrongArgumentCount(name, "1개 또는 2개")
            };
        }

        private static double RoundWithDigits(string name, double value, double digitsValue)
        {
            var digits = ToInteger(digitsValue, name, "자릿수");
            if (digits is < 0 or > 15)
            {
                throw new CalculationException("invalid_argument", $"{name}: 자릿수는 0 이상 15 이하의 정수여야 합니다.");
            }

            return Math.Round(value, digits, MidpointRounding.AwayFromZero);
        }

        private static double Root(double value, double degreeValue)
        {
            var degree = ToInteger(degreeValue, "root", "차수");
            if (degree == 0)
            {
                throw new CalculationException("invalid_argument", "root: 근의 차수는 0일 수 없습니다.");
            }

            if (value < 0)
            {
                if (degree % 2 == 0)
                {
                    throw new CalculationException("invalid_argument", "root: 음수의 짝수근은 실수 범위에서 계산할 수 없습니다.");
                }

                return -Math.Pow(Math.Abs(value), 1d / degree);
            }

            return Math.Pow(value, 1d / degree);
        }

        private double Log(string name, IReadOnlyList<double> args)
        {
            return args.Count switch
            {
                1 => RequireDomain(args[0] > 0, name, "0보다 큰 값이 필요합니다.", Math.Log(args[0])),
                2 => RequireDomain(
                    args[0] > 0 && args[1] > 0 && args[1] != 1,
                    name,
                    "값과 밑은 0보다 커야 하며 밑은 1일 수 없습니다.",
                    Math.Log(args[0], args[1])),
                _ => throw WrongArgumentCount(name, "1개 또는 2개")
            };
        }

        private double Clamp(string name, IReadOnlyList<double> args)
        {
            if (args.Count != 3)
            {
                throw WrongArgumentCount(name, "3개");
            }

            if (args[1] > args[2])
            {
                throw Error("invalid_argument", "clamp의 최솟값은 최댓값보다 클 수 없습니다.");
            }

            return Math.Clamp(args[0], args[1], args[2]);
        }

        private static double OneArg(string name, IReadOnlyList<double> args, Func<double, double> function)
        {
            if (args.Count != 1)
            {
                throw WrongArgumentCount(name, "1개");
            }

            return function(args[0]);
        }

        private static double TwoArgs(string name, IReadOnlyList<double> args, Func<double, double, double> function)
        {
            if (args.Count != 2)
            {
                throw WrongArgumentCount(name, "2개");
            }

            return function(args[0], args[1]);
        }

        private static double ManyArgs(string name, IReadOnlyList<double> args, Func<IReadOnlyList<double>, double> function)
        {
            if (args.Count == 0)
            {
                throw WrongArgumentCount(name, "1개 이상");
            }

            return function(args);
        }

        private static double RequireDomain(bool condition, string name, string message, double value)
        {
            if (!condition)
            {
                throw new CalculationException("invalid_argument", $"{name}: {message}");
            }

            return value;
        }

        private static double Factorial(double value)
        {
            var n = ToInteger(value, "factorial", "값");
            if (n < 0)
            {
                throw new CalculationException("invalid_argument", "factorial: 0 이상의 정수가 필요합니다.");
            }

            if (n > 170)
            {
                throw new CalculationException("result_too_large", "factorial: 170 이하의 정수만 지원합니다.");
            }

            var result = 1d;
            for (var i = 2; i <= n; i++)
            {
                result *= i;
            }

            return result;
        }

        private static double Combination(double nValue, double kValue)
        {
            var n = ToInteger(nValue, "choose", "n");
            var k = ToInteger(kValue, "choose", "k");

            if (n < 0 || k < 0 || k > n)
            {
                throw new CalculationException("invalid_argument", "choose: 0 <= k <= n 조건을 만족하는 정수가 필요합니다.");
            }

            k = Math.Min(k, n - k);
            var result = 1d;
            for (var i = 1; i <= k; i++)
            {
                result = result * (n - k + i) / i;
            }

            return result;
        }

        private static double Permutation(double nValue, double kValue)
        {
            var n = ToInteger(nValue, "perm", "n");
            var k = ToInteger(kValue, "perm", "k");

            if (n < 0 || k < 0 || k > n)
            {
                throw new CalculationException("invalid_argument", "perm: 0 <= k <= n 조건을 만족하는 정수가 필요합니다.");
            }

            var result = 1d;
            for (var i = 0; i < k; i++)
            {
                result *= n - i;
            }

            return result;
        }

        private static int ToInteger(double value, string functionName, string argumentName)
        {
            if (!double.IsFinite(value) || Math.Abs(value - Math.Round(value)) > 1e-9)
            {
                throw new CalculationException("invalid_argument", $"{functionName}: {argumentName}은 정수여야 합니다.");
            }

            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new CalculationException("invalid_argument", $"{functionName}: {argumentName}이 지원 범위를 벗어났습니다.");
            }

            return (int)Math.Round(value);
        }

        private static CalculationException WrongArgumentCount(string functionName, string expected)
        {
            return new CalculationException("wrong_argument_count", $"{functionName}: 인자는 {expected}여야 합니다.");
        }

        private void SkipWhitespace()
        {
            while (char.IsWhiteSpace(Current))
            {
                m_Index++;
            }
        }

        private bool Match(char expected)
        {
            if (Current != expected)
            {
                return false;
            }

            m_Index++;
            return true;
        }

        private void Expect(char expected, string code, string message)
        {
            if (!Match(expected))
            {
                throw Error(code, message);
            }
        }

        private void GuardDepth(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error("expression_too_deep", "수식 중첩이 너무 깊습니다.");
            }
        }

        private void CountOperation()
        {
            m_Operations++;
            if (m_Operations > MaxOperations)
            {
                throw Error("expression_too_complex", "수식이 너무 복잡합니다.");
            }
        }

        private CalculationException Error(string code, string message)
        {
            return new CalculationException(code, $"{message} (위치 {m_Index + 1})");
        }

        private static bool IsIdentifierStart(char value)
        {
            return char.IsAsciiLetter(value) || value == '_';
        }

        private static bool IsIdentifierPart(char value)
        {
            return char.IsAsciiLetterOrDigit(value) || value == '_';
        }
    }

    private sealed class CalculationException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
