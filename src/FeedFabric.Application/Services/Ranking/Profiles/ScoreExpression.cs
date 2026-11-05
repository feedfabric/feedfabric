using System.Globalization;

namespace FeedFabric.Application.Services.Ranking.Profiles;

// A custom scoring formula, as a profile's Scoring.Expression, e.g. "model * decay_exp(age_hours, 12)".
// It's parsed once into a small tree and evaluated per post; it can only read the variables below and call the
// functions below, so a profile can't run anything else. Numbers, + - * / ^, comparisons (which give 1 or 0),
// and/or/not, and parentheses.
public sealed class ScoreExpression
{
    public const int MaxLength = 500;

    // Declared before Variables, which reads it: static fields initialise in order
    private static readonly Dictionary<string, string> FeatureDescriptions = new()
    {
        ["age_minutes"] = "Minutes since it was posted",
        ["follows_author"] = "1 if the viewer follows the author, otherwise 0",
        ["author_affinity"] = "How much the viewer engages with the author (likes count 1, comments 2)",
        ["topic_match"] = "0 to 1: how much of what the viewer engages with is on the post's topics",
        ["likes"] = "Number of likes",
        ["comments"] = "Number of comments",
        ["impressions"] = "Number of times it's been seen",
        ["opens"] = "Number of times it's been opened",
        ["ctr"] = "Share of views that opened it (smoothed for new posts)",
        ["engagement_rate"] = "Share of views that liked or commented (smoothed for new posts)",
        ["avg_dwell_s"] = "Average seconds on screen",
        ["author_followers_log"] = "ln(1 + the author's followers)",
        ["has_media"] = "1 if it has photos, otherwise 0",
        ["content_length"] = "Characters of text",
        ["velocity_30m"] = "Likes and comments in the last 30 minutes",
        ["viewer_author_ctr"] = "How often the viewer opens this author's posts (smoothed)",
        ["viewer_author_seen"] = "How many of the author's posts the viewer saw in the last week",
        ["session_topic_match"] = "0 to 1: match with what the viewer engaged with in the last 15 minutes",
        ["semantic_match"] = "0 to 1: how close in meaning it is to the viewer's recent likes",
    };

    // Variables beyond the ranking features, in the order they follow them in the values array
    public static readonly string[] ExtraVariables = ["age_hours", "formula", "model"];

    public static readonly IReadOnlyList<VariableInfo> Variables =
    [
        .. RankingFeatures.Names.Where(n => n != "position").Select(n => new VariableInfo(n, FeatureDescriptions.GetValueOrDefault(n, n))),
        new("age_hours", "Hours since it was posted"),
        new("formula", "The built-in scoring formula's score for the post (1 or more for most posts)"),
        new("model", "The trained model's value for the post using the profile's goals, shifted to be 0 or more; the formula's score until a model is loaded"),
    ];

    public static readonly IReadOnlyList<FunctionInfo> Functions =
    [
        new("decay_exp", 2, "decay_exp(t, half_life)", "1 at t = 0, halving every half_life"),
        new("decay_linear", 2, "decay_linear(t, zero_at)", "1 at t = 0, falling in a straight line to 0 at zero_at"),
        new("decay_gauss", 2, "decay_gauss(t, scale)", "1 at t = 0, a gentle bell curve that's 0.5 at t = scale"),
        new("min", 2, "min(a, b)", "The smaller of a and b"),
        new("max", 2, "max(a, b)", "The larger of a and b"),
        new("clamp", 3, "clamp(x, low, high)", "x, kept between low and high"),
        new("if", 3, "if(condition, then, else)", "then when condition is not 0, otherwise else"),
        new("ln", 1, "ln(x)", "Natural log; 0 for x of 0 or less"),
        new("log1p", 1, "log1p(x)", "ln(1 + x): grows quickly at first, then slowly; good for counts"),
        new("sqrt", 1, "sqrt(x)", "Square root; 0 for negative x"),
        new("exp", 1, "exp(x)", "e to the power of x"),
        new("abs", 1, "abs(x)", "x without its sign"),
    ];

    // Starting points shown in the studio; tests check each does what its label says (FormulaIntentTests)
    public static readonly IReadOnlyList<ExampleFormula> Examples =
    [
        new("Model, fading over 12 hours", "model * decay_exp(age_hours, 12)"),
        new("Popular now", "log1p(likes + 2 * comments) * decay_exp(age_hours, 6)"),
        new("Conversations", "formula * (1 + log1p(comments))"),
        new("Your topics only", "if(topic_match > 0.2, formula, formula * 0.2)"),
        new("Trending", "formula * (1 + 0.3 * log1p(velocity_30m)) * decay_exp(age_hours, 3)"),
    ];

    private static readonly Dictionary<string, int> VariableIndex = RankingFeatures.Names.Concat(ExtraVariables)
        .Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);

    private static readonly Dictionary<string, FunctionInfo> FunctionsByName = Functions.ToDictionary(f => f.Name);

    private readonly Node root;

    private ScoreExpression(string text, Node root, bool usesModel)
    {
        Text = text;
        this.root = root;
        UsesModel = usesModel;
    }

    public string Text { get; }

    // Only score with the model when the formula asks for it
    public bool UsesModel { get; }

    // How many values Evaluate expects: every ranking feature, then ExtraVariables
    public static int Width => VariableIndex.Count;

    public static int IndexOf(string variable) => VariableIndex[variable];

    public static bool TryParse(string text, out ScoreExpression? expression, out ExpressionError? error)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = new ExpressionError("The formula is empty.", 0);
            return false;
        }
        if (text.Length > MaxLength)
        {
            error = new ExpressionError($"The formula is longer than {MaxLength} characters.", MaxLength);
            return false;
        }

        try
        {
            var parser = new Parser(text);
            var root = parser.ParseAll();
            expression = new ScoreExpression(text, root, parser.UsesModel);
            error = null;
            return true;
        }
        catch (ParseException ex)
        {
            error = new ExpressionError(ex.Message, ex.Position);
            return false;
        }
    }

    // values: Width numbers, in RankingFeatures.Names order then ExtraVariables. Never NaN or infinite.
    public double Evaluate(double[] values)
    {
        var result = root.Evaluate(values);
        return double.IsFinite(result) ? result : 0;
    }

    private abstract record Node
    {
        public abstract double Evaluate(double[] values);
    }

    private sealed record Number(double Value) : Node
    {
        public override double Evaluate(double[] values) => Value;
    }

    private sealed record Variable(int Index) : Node
    {
        public override double Evaluate(double[] values) => values[Index];
    }

    private sealed record Unary(char Op, Node Operand) : Node
    {
        public override double Evaluate(double[] values)
        {
            var x = Operand.Evaluate(values);
            return Op == '-' ? -x : x == 0 ? 1 : 0;
        }
    }

    private sealed record Binary(string Op, Node Left, Node Right) : Node
    {
        public override double Evaluate(double[] values)
        {
            var a = Left.Evaluate(values);
            // and/or only look at the right when they need to
            if (Op == "and")
            {
                return a != 0 && Right.Evaluate(values) != 0 ? 1 : 0;
            }
            if (Op == "or")
            {
                return a != 0 || Right.Evaluate(values) != 0 ? 1 : 0;
            }

            var b = Right.Evaluate(values);
            return Op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b == 0 ? 0 : a / b,
                "^" => Math.Pow(a, b),
                ">" => a > b ? 1 : 0,
                "<" => a < b ? 1 : 0,
                ">=" => a >= b ? 1 : 0,
                "<=" => a <= b ? 1 : 0,
                "==" => a == b ? 1 : 0,
                "!=" => a != b ? 1 : 0,
                _ => throw new InvalidOperationException(Op),
            };
        }
    }

    private sealed record Call(string Name, Node[] Args) : Node
    {
        public override double Evaluate(double[] values)
        {
            if (Name == "if")
            {
                return Args[0].Evaluate(values) != 0 ? Args[1].Evaluate(values) : Args[2].Evaluate(values);
            }

            var x = Args.Select(a => a.Evaluate(values)).ToArray();
            return Name switch
            {
                // Time decays treat a scale of 0 or less as "no decay"
                "decay_exp" => x[1] <= 0 ? 1 : Math.Pow(0.5, Math.Max(0, x[0]) / x[1]),
                "decay_linear" => x[1] <= 0 ? 1 : Math.Max(0, 1 - Math.Max(0, x[0]) / x[1]),
                "decay_gauss" => x[1] <= 0 ? 1 : Math.Pow(0.5, Math.Pow(Math.Max(0, x[0]) / x[1], 2)),
                "min" => Math.Min(x[0], x[1]),
                "max" => Math.Max(x[0], x[1]),
                "clamp" => Math.Clamp(x[0], Math.Min(x[1], x[2]), Math.Max(x[1], x[2])),
                "ln" => x[0] <= 0 ? 0 : Math.Log(x[0]),
                "log1p" => x[0] <= -1 ? 0 : Math.Log(1 + x[0]),
                "sqrt" => x[0] <= 0 ? 0 : Math.Sqrt(x[0]),
                "exp" => Math.Exp(Math.Min(x[0], 50)),
                "abs" => Math.Abs(x[0]),
                _ => throw new InvalidOperationException(Name),
            };
        }
    }

    // Precedence, loosest first: or, and, comparisons, + -, * /, unary - and not, ^ (right to left)
    private sealed class Parser(string text)
    {
        private const int MaxDepth = 40;
        private int pos;
        private int depth;

        public bool UsesModel { get; private set; }

        public Node ParseAll()
        {
            var node = ParseOr();
            SkipSpace();
            if (pos < text.Length)
            {
                throw new ParseException($"Unexpected '{text[pos]}'.", pos);
            }
            return node;
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (TryWord("or"))
            {
                left = new Binary("or", left, ParseAnd());
            }
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseComparison();
            while (TryWord("and"))
            {
                left = new Binary("and", left, ParseComparison());
            }
            return left;
        }

        private Node ParseComparison()
        {
            var left = ParseSum();
            foreach (var op in new[] { ">=", "<=", "==", "!=", ">", "<" })
            {
                if (TrySymbol(op))
                {
                    return new Binary(op, left, ParseSum());
                }
            }
            return left;
        }

        private Node ParseSum()
        {
            var left = ParseProduct();
            while (true)
            {
                if (TrySymbol("+"))
                {
                    left = new Binary("+", left, ParseProduct());
                }
                else if (TrySymbol("-"))
                {
                    left = new Binary("-", left, ParseProduct());
                }
                else
                {
                    return left;
                }
            }
        }

        private Node ParseProduct()
        {
            var left = ParseUnary();
            while (true)
            {
                if (TrySymbol("*"))
                {
                    left = new Binary("*", left, ParseUnary());
                }
                else if (TrySymbol("/"))
                {
                    left = new Binary("/", left, ParseUnary());
                }
                else
                {
                    return left;
                }
            }
        }

        private Node ParseUnary()
        {
            if (++depth > MaxDepth)
            {
                throw new ParseException("The formula is nested too deeply.", pos);
            }
            try
            {
                if (TrySymbol("-"))
                {
                    return new Unary('-', ParseUnary());
                }
                if (TryWord("not"))
                {
                    return new Unary('!', ParseUnary());
                }
                return ParsePower();
            }
            finally
            {
                depth--;
            }
        }

        private Node ParsePower()
        {
            var baseNode = ParseAtom();
            return TrySymbol("^") ? new Binary("^", baseNode, ParseUnary()) : baseNode;
        }

        private Node ParseAtom()
        {
            SkipSpace();
            if (pos >= text.Length)
            {
                throw new ParseException("The formula ends too soon.", pos);
            }

            var c = text[pos];
            if (char.IsDigit(c) || c == '.')
            {
                var start = pos;
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.'))
                {
                    pos++;
                }
                if (!double.TryParse(text[start..pos], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    throw new ParseException($"'{text[start..pos]}' isn't a number.", start);
                }
                return new Number(number);
            }

            if (c == '(')
            {
                pos++;
                depth++;
                var inner = ParseOr();
                depth--;
                Expect(')');
                return inner;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = pos;
                var name = ReadName();
                if (TrySymbol("("))
                {
                    if (!FunctionsByName.TryGetValue(name, out var function))
                    {
                        throw new ParseException($"There's no function called '{name}'.", start);
                    }
                    var args = new List<Node>();
                    if (!TrySymbol(")"))
                    {
                        do
                        {
                            args.Add(ParseOr());
                        } while (TrySymbol(","));
                        Expect(')');
                    }
                    if (args.Count != function.Arity)
                    {
                        throw new ParseException($"{function.Signature} takes {function.Arity} value{(function.Arity == 1 ? "" : "s")}, not {args.Count}.", start);
                    }
                    return new Call(name, args.ToArray());
                }

                if (name == "position" || !VariableIndex.TryGetValue(name, out var index))
                {
                    throw new ParseException($"There's no variable called '{name}'.", start);
                }
                UsesModel |= name == "model";
                return new Variable(index);
            }

            throw new ParseException($"Unexpected '{c}'.", pos);
        }

        private string ReadName()
        {
            var start = pos;
            while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_'))
            {
                pos++;
            }
            return text[start..pos].ToLowerInvariant();
        }

        private bool TryWord(string word)
        {
            SkipSpace();
            if (pos + word.Length <= text.Length
                && string.Compare(text, pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (pos + word.Length == text.Length || !(char.IsLetterOrDigit(text[pos + word.Length]) || text[pos + word.Length] == '_')))
            {
                pos += word.Length;
                return true;
            }
            return false;
        }

        private bool TrySymbol(string symbol)
        {
            SkipSpace();
            if (string.CompareOrdinal(text, pos, symbol, 0, symbol.Length) != 0)
            {
                return false;
            }
            // ">" isn't the start of ">="
            if (symbol.Length == 1 && symbol is ">" or "<" && pos + 1 < text.Length && text[pos + 1] == '=')
            {
                return false;
            }
            pos += symbol.Length;
            return true;
        }

        private void Expect(char c)
        {
            SkipSpace();
            if (pos >= text.Length || text[pos] != c)
            {
                throw new ParseException($"Expected '{c}'.", pos);
            }
            pos++;
        }

        private void SkipSpace()
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }
        }
    }

    private sealed class ParseException(string message, int position) : Exception(message)
    {
        public int Position { get; } = position;
    }
}

public sealed record VariableInfo(string Name, string Description);

public sealed record FunctionInfo(string Name, int Arity, string Signature, string Description);

public sealed record ExampleFormula(string Label, string Text);

public sealed record ExpressionError(string Message, int Position);
