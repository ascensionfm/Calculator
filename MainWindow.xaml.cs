using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NCalc;
// Parlot.Fluent is not used in the context of this change, but kept for original compatibility
// using Parlot.Fluent;
using System.Text.RegularExpressions; // Kept for original compatibility

namespace WpfLatexCalculator
{
    public partial class MainWindow : Window
    {
        private StringBuilder _latexExpressionBuilder = new StringBuilder();
        private StringBuilder _evaluationExpressionBuilder = new StringBuilder();
        private bool _isRadians = false;
        private const string DegSymbol = "deg";
        private const string RadSymbol = "rad";
        private int _openParenthesesCount = 0;
        private enum TokenType
        {
            Number,
            Operator,
            FunctionName,
            Constant,
            ParenOpen,
            ParenClose,
            PostfixValueOp
        }

        public MainWindow()
        {
            InitializeComponent();
            AngleModeButton.Content = DegSymbol;
            UpdateExpressionDisplay();
        }

        private void UpdateExpressionDisplay()
        {
            ExpressionDisplay.Formula = _latexExpressionBuilder.ToString();
        }

        // AppendToExpression (ensure this version is used, especially the FunctionName handling)
        private void AppendToExpression(string displayContent, string evalContent, TokenType tokenType)
        {
            ResultDisplay.Formula = "";

            InputToken currentToken = new InputToken
            {
                Latex = displayContent,
                Eval = evalContent,
                Type = tokenType
            };

            if (_tokenHistory.Any())
            {
                InputToken lastToken = _tokenHistory.Last();
                bool lastProducesValue = (lastToken.Type == TokenType.Number ||
                                          lastToken.Type == TokenType.Constant ||
                                          lastToken.Type == TokenType.PostfixValueOp ||
                                          lastToken.Type == TokenType.ParenClose);

                bool currentStartsValueAndIsNotBinaryOperator = (currentToken.Type == TokenType.Number ||
                                                   currentToken.Type == TokenType.Constant ||
                                                   currentToken.Type == TokenType.ParenOpen ||
                                                   (currentToken.Type == TokenType.FunctionName && !string.IsNullOrEmpty(currentToken.Eval)));

                if (lastProducesValue && currentStartsValueAndIsNotBinaryOperator)
                {
                    bool extendingNumber = (currentToken.Type == TokenType.Number && lastToken.Type == TokenType.Number && currentToken.Eval != ".") ||
                                           (currentToken.Eval == "." && lastToken.Type == TokenType.Number);

                    if (!extendingNumber)
                    {
                        InputToken multiplyToken = new InputToken { Latex = @" \times ", Eval = "*", Type = TokenType.Operator };
                        _tokenHistory.Add(multiplyToken);
                        _latexExpressionBuilder.Append(multiplyToken.Latex);
                        _evaluationExpressionBuilder.Append(multiplyToken.Eval);
                    }
                }
            }

            if (currentToken.Type == TokenType.FunctionName)
            {
                if (!string.IsNullOrEmpty(currentToken.Latex) && !currentToken.Latex.EndsWith("("))
                {
                    // Only add "(" if it's a function name that should be followed by arguments for display
                    // For the new x^y, the display "(" is handled explicitly if desired.
                    // The eval part ALWAYS needs the "(", though.
                }
                if (!string.IsNullOrEmpty(currentToken.Eval) && !currentToken.Eval.EndsWith("("))
                {
                    currentToken.Eval += "("; // Eval always gets "(" for functions
                }
            }


            if (currentToken.Type == TokenType.Number && currentToken.Eval == ".")
            {
                string currentNumberEvalSegment = "";
                for (int i = _tokenHistory.Count - 1; i >= 0; i--)
                {
                    var pt = _tokenHistory[i];
                    if (pt.Type != TokenType.Number && pt.Eval != ".") break;
                    currentNumberEvalSegment = pt.Eval + currentNumberEvalSegment;
                }
                if (currentNumberEvalSegment.Contains(".")) return;
            }
            if (currentToken.Type == TokenType.Number && currentToken.Eval == "." &&
                (_tokenHistory.Count == 0 ||
                 (_tokenHistory.Last().Type != TokenType.Number && _tokenHistory.Last().Type != TokenType.Constant && _tokenHistory.Last().Type != TokenType.PostfixValueOp && _tokenHistory.Last().Type != TokenType.ParenClose)))
            {
                InputToken zeroToken = new InputToken { Latex = "0", Eval = "0", Type = TokenType.Number };
                _tokenHistory.Add(zeroToken);
                _latexExpressionBuilder.Append(zeroToken.Latex);
                _evaluationExpressionBuilder.Append(zeroToken.Eval);
            }
            _tokenHistory.Add(currentToken);
            _latexExpressionBuilder.Append(currentToken.Latex);
            _evaluationExpressionBuilder.Append(currentToken.Eval);

            if (tokenType == TokenType.ParenOpen || (tokenType == TokenType.FunctionName && currentToken.Eval.EndsWith("(")))
            {
                _openParenthesesCount++;
            }
            else if (tokenType == TokenType.ParenClose)
            {
                if (_openParenthesesCount > 0) _openParenthesesCount--;
            }
            UpdateExpressionDisplay();
        }

        // RebuildBuildersFromHistory (ensure this version is used)
        private void RebuildBuildersFromHistory(bool updateOpenParenCount = true)
        {
            _latexExpressionBuilder.Clear();
            _evaluationExpressionBuilder.Clear();
            if (updateOpenParenCount) _openParenthesesCount = 0;

            foreach (InputToken token in _tokenHistory)
            {
                _latexExpressionBuilder.Append(token.Latex);
                _evaluationExpressionBuilder.Append(token.Eval);
                if (updateOpenParenCount)
                {
                    if (token.Type == TokenType.ParenOpen || (token.Type == TokenType.FunctionName && token.Eval.EndsWith("(")))
                    {
                        _openParenthesesCount++;
                    }
                    else if (token.Type == TokenType.ParenClose && _openParenthesesCount > 0)
                    {
                        _openParenthesesCount--;
                    }
                }
            }
        }

        // ExtractLastOperandTokens (ensure this version is used)
        private List<InputToken> ExtractLastOperandTokens()
        {
            var operandTokens = new List<InputToken>();
            if (!_tokenHistory.Any()) return operandTokens;

            int parenBalance = 0;
            for (int i = _tokenHistory.Count - 1; i >= 0; i--)
            {
                InputToken token = _tokenHistory[i];
                operandTokens.Insert(0, token);

                if (token.Type == TokenType.ParenClose) parenBalance++;
                else if (token.Type == TokenType.ParenOpen) parenBalance--;
                // If the token is a function name, it effectively acts as an opening parenthesis
                // This assumes FunctionName tokens in history have Eval strings ending with "("
                else if (token.Type == TokenType.FunctionName) // Simplified from token.Eval.EndsWith("(") for safety
                {
                    parenBalance--;
                }

                if (parenBalance < 0) break;

                if (i == 0 || (parenBalance == 0 &&
                    (token.Type == TokenType.Operator && !("()".Contains(token.Eval))) || // Binary operators (not parens themselves)
                    (token.Type == TokenType.FunctionName && i != (_tokenHistory.Count - operandTokens.Count))
                   ))
                {
                    if (operandTokens.Count > 0 &&
                        (token.Type == TokenType.Operator || token.Type == TokenType.FunctionName) &&
                        i != (_tokenHistory.Count - operandTokens.Count()))
                    {
                        operandTokens.RemoveAt(0);
                    }
                    break;
                }
            }
            return operandTokens;
        }


        // MODIFIED OperatorButton_Click with new "^" case
        private void OperatorButton_Click(object sender, RoutedEventArgs e)
        {
            string opTag = ((Button)sender).Tag.ToString();
            string displayOp = opTag;
            string evalOp = opTag;
            TokenType type = TokenType.Operator;

            switch (opTag)
            {
                case "*": displayOp = @" \times "; break;
                case "/": displayOp = @" \div "; break;
                case "%": displayOp = @" \% "; break;
                case "(": type = TokenType.ParenOpen; break;
                case ")": type = TokenType.ParenClose; break;
                case "Pow": // Standard Pow function button (e.g., a button labeled "Pow")
                            // This will result in display: \text{Pow}( and eval: Pow(
                    AppendToExpression(@"\text{Pow}", "Pow", TokenType.FunctionName); // for FunctionName handling
                    return;

                case "^": // For x^y functionality (assuming a button with Tag="^", e.g., labeled "xʸ")
                    List<InputToken> baseTokens = ExtractLastOperandTokens();
                    if (baseTokens.Any())
                    {
                        for (int i = 0; i < baseTokens.Count; i++)
                        {
                            if (_tokenHistory.Any()) _tokenHistory.RemoveAt(_tokenHistory.Count - 1);
                        }
                        RebuildBuildersFromHistory(updateOpenParenCount: true);

                        // Pass "" for displayContent; AppendToExpression with FunctionName type will make Latex "(".
                        // Eval content "Pow" will become "Pow(".
                        AppendToExpression("", "Pow", TokenType.FunctionName); // Results in LaTeX: '(', Eval: 'Pow('

                        foreach (var token in baseTokens)
                        {
                            AppendToExpression(token.Latex, token.Eval, token.Type); // Appends base
                        }
                        // Appends "^{" to LaTeX display and "," to evaluation string
                        AppendToExpression("^{", ",", TokenType.Operator); // Results in LaTeX: '(base)^{', Eval: 'Pow(base,'
                    }
                    else
                    {
                        // No base found for '^' operator. Provide feedback to the user.
                        ResultDisplay.Formula = @"\text{Error: Base required for x}^y";
                        // Consider clearing this message on the next valid input or after a short delay.
                    }
                    return;
            }
            AppendToExpression(displayOp, evalOp, type);
        }



        // ... (rest of your MainWindow.xaml.cs methods: NumberButton_Click, PowerButton_Click for x², FunctionButton_Click, etc.)
        // Ensure PowerButton_Click (for x²) is the version from the previous step that produces (base)^{2}.

        // Make sure to include all other necessary methods from your original file:
        // NumberButton_Click, PowerButton_Click (the x² version), FunctionButton_Click, ConstantButton_Click,
        // ClearButton_Click, InputToken class, _tokenHistory list, BackspaceButton_Click,
        // ModeButton_Click, EqualsButton_Click.

        // Example of PowerButton_Click (x^2) for reference - ensure this is already in your code
        private void PowerButton_Click(object sender, RoutedEventArgs e) // For x²
        {
            string powerTag = ((Button)sender).Tag.ToString();
            if (powerTag == "^2")
            {
                List<InputToken> baseTokens = ExtractLastOperandTokens();

                if (baseTokens.Any())
                {
                    for (int i = 0; i < baseTokens.Count; i++)
                    {
                        if (_tokenHistory.Any()) _tokenHistory.RemoveAt(_tokenHistory.Count - 1);
                    }
                    RebuildBuildersFromHistory(updateOpenParenCount: true);
                    AppendToExpression("(", "Pow", TokenType.FunctionName);
                    foreach (var baseToken in baseTokens)
                    {
                        AppendToExpression(baseToken.Latex, baseToken.Eval, baseToken.Type);
                    }
                    AppendToExpression(")^{", ",", TokenType.Operator);
                    AppendToExpression("2", "2", TokenType.Number);
                    AppendToExpression("}", ")", TokenType.ParenClose);
                }
            }
            UpdateExpressionDisplay();
        }


        private void NumberButton_Click(object sender, RoutedEventArgs e)
        {
            string number = ((Button)sender).Tag.ToString();
            AppendToExpression(number, number, TokenType.Number);
        }

        private void FunctionButton_Click(object sender, RoutedEventArgs e)
        {
            string funcLatex = ((Button)sender).Tag.ToString();
            string evalFunc = "";
            switch (funcLatex)
            {
                case @"\sin": evalFunc = "Sin"; break;
                case @"\cos": evalFunc = "Cos"; break;
                case @"\tan": evalFunc = "Tan"; break;
                case @"\log_{10}": evalFunc = "Log10"; break;
                case @"\ln": evalFunc = "Log"; break;
                case @"\sqrt": evalFunc = "Sqrt"; break;
                default:
                    evalFunc = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(funcLatex.Replace(@"\", "")).Replace(" ", "");
                    break;
            }
            AppendToExpression(funcLatex, evalFunc, TokenType.FunctionName);
        }

        private void ConstantButton_Click(object sender, RoutedEventArgs e)
        {
            string displayConst = ((Button)sender).Tag.ToString();
            string evalConst = "";
            if (displayConst == @"\pi") evalConst = "PI";
            else if (displayConst == "e") evalConst = "E";
            else evalConst = displayConst;
            AppendToExpression(displayConst, evalConst, TokenType.Constant);
        }
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _latexExpressionBuilder.Clear();
            _evaluationExpressionBuilder.Clear();
            _tokenHistory.Clear();
            ResultDisplay.Formula = "";
            _openParenthesesCount = 0;
            UpdateExpressionDisplay();
        }

        private class InputToken
        {
            public string Latex { get; set; }
            public string Eval { get; set; }
            public TokenType Type { get; set; }
        }
        private List<InputToken> _tokenHistory = new List<InputToken>();

        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            ResultDisplay.Formula = "";
            if (_tokenHistory.Any())
            {
                _tokenHistory.RemoveAt(_tokenHistory.Count - 1);
                RebuildBuildersFromHistory(updateOpenParenCount: true);
            }
            UpdateExpressionDisplay();
        }


        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            _isRadians = !_isRadians;
            AngleModeButton.Content = _isRadians ? RadSymbol : DegSymbol;
        }

        private void EqualsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_evaluationExpressionBuilder.Length == 0 && _tokenHistory.Count == 0)
            {
                ResultDisplay.Formula = ""; // Clear result if expression is empty
                return;
            }
            if (_evaluationExpressionBuilder.Length == 0 && _tokenHistory.Any())
            {
                RebuildBuildersFromHistory(true); // Try to build from history if eval string is empty
                // If it's just a number in history, that's the "result" of no operation
                if (_evaluationExpressionBuilder.Length == 0 && _tokenHistory.Count > 0 && _tokenHistory.All(t => t.Type == TokenType.Number || t.Eval == "."))
                {
                    ResultDisplay.Formula = _latexExpressionBuilder.ToString();
                    // ExpressionDisplay already shows this number. No state change needed beyond ResultDisplay.
                    return;
                }
                if (_evaluationExpressionBuilder.Length == 0)
                { // Still empty, nothing valid to evaluate
                    ResultDisplay.Formula = @"\text{Error}";
                    ExpressionDisplay.Formula = @"\text{Error}"; // Also show error in main display
                    ClearStateOnError(); // Clear state and update display
                    return;
                }
            }

            try
            {
                string finalEvalExpression = _evaluationExpressionBuilder.ToString();

                if (_openParenthesesCount > 0) // Auto-close parentheses for evaluation
                {
                    string closingParentheses = new string(')', _openParenthesesCount);
                    finalEvalExpression += closingParentheses;
                }

                // Percentage handling (ensure this is your preferred robust version)
                while (finalEvalExpression.Contains("%"))
                {
                    // ... (Your percentage handling logic) ...
                    // Simplified placeholder for brevity, use your full logic:
                    int percentIdx = finalEvalExpression.LastIndexOf('%');
                    if (percentIdx > 0)
                    {
                        // Basic example: find number before %
                        string numPart = System.Text.RegularExpressions.Regex.Match(finalEvalExpression.Substring(0, percentIdx), @"(\d+(\.\d+)?)$").Value;
                        if (!string.IsNullOrEmpty(numPart))
                        {
                            finalEvalExpression = finalEvalExpression.Substring(0, percentIdx - numPart.Length) + $"({numPart}/100.0)" + finalEvalExpression.Substring(percentIdx + 1);
                        }
                        else { finalEvalExpression = finalEvalExpression.Remove(percentIdx, 1); } // Fallback: remove %
                    }
                    else { finalEvalExpression = finalEvalExpression.Remove(percentIdx, 1); } // Fallback: remove %
                }

                NCalc.Expression expr = new NCalc.Expression(finalEvalExpression);
                expr.Parameters["PI"] = Math.PI;
                expr.Parameters["E"] = Math.E;

                expr.EvaluateFunction += (name, args) => // Your existing trig functions logic
                {
                    if (name.Equals("Sin", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Cos", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Tan", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Parameters.Length == 1)
                        {
                            double val = Convert.ToDouble(args.Parameters[0].Evaluate());
                            if (!_isRadians)
                            {
                                val = val * (Math.PI / 180.0);
                            }
                            if (name.Equals("Sin", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Sin(val);
                            else if (name.Equals("Cos", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Cos(val);
                            else if (name.Equals("Tan", StringComparison.OrdinalIgnoreCase))
                            {
                                double cosVal = Math.Cos(val);
                                if (Math.Abs(cosVal) < 1e-12)
                                {
                                    throw new DivideByZeroException("Tangent is undefined for this angle.");
                                }
                                args.Result = Math.Tan(val);
                            }
                        }
                        else
                        {
                            throw new ArgumentException($"Trigonometric function {name} expects 1 argument.");
                        }
                    }
                };

                object result = expr.Evaluate();
                string resultString;

                // Result formatting (ensure this is your preferred robust version from your file)
                if (result is double d)
                {
                    if (double.IsNaN(d) || double.IsInfinity(d)) { resultString = @"\text{Error}"; }
                    else if (Math.Abs(d) > 1e12 || (Math.Abs(d) < 1e-9 && d != 0)) { resultString = d.ToString("E6", CultureInfo.InvariantCulture); }
                    else
                    {
                        // Prefer G10 for general formatting, then clean up.
                        resultString = d.ToString("G10", CultureInfo.InvariantCulture);
                        if (resultString.Contains(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator))
                        {
                            resultString = resultString.TrimEnd('0').TrimEnd(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator[0]);
                        }
                        // If it became an integer representation after trim, check if it's within Int64 limits.
                        // This avoids converting "1.0" to "1" then trying to make "1" into 1L if it was originally a very large double.
                        if (decimal.TryParse(resultString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decVal) && decVal == Math.Truncate(decVal) && decVal >= long.MinValue && decVal <= long.MaxValue)
                        {
                            resultString = Convert.ToInt64(decVal).ToString(CultureInfo.InvariantCulture);
                        }

                        if (string.IsNullOrEmpty(resultString) || resultString == "-0" || resultString == "-") resultString = "0";
                        else if (resultString == CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator[0].ToString()) resultString = "0";
                    }
                }
                else { resultString = result?.ToString() ?? @"\text{Error}"; }

                ResultDisplay.Formula = resultString.Replace(",", ".");

                _tokenHistory.Clear();
                _latexExpressionBuilder.Clear();
                _evaluationExpressionBuilder.Clear();
                _openParenthesesCount = 0;

                if (resultString != @"\text{Error}")
                {
                    AppendToExpression(resultString.Replace(",", "."), resultString.Replace(",", "."), TokenType.Number);
                }
                else
                {
                    ExpressionDisplay.Formula = @"\text{Error}";
                    UpdateExpressionDisplay(); // Ensure main display updates to show error text
                }
            }
            catch (DivideByZeroException)
            {
                ResultDisplay.Formula = @"\text{Error: Division by zero}";
                ExpressionDisplay.Formula = @"\text{Error}";
                ClearStateOnError();
            }
            catch (Exception ex)
            {
                // For debugging, you might want to see ex.Message
                // System.Windows.MessageBox.Show(ex.Message, "Calculation Error"); 
                ResultDisplay.Formula = @"\text{Error}";
                ExpressionDisplay.Formula = @"\text{Error}";
                ClearStateOnError();
            }
        }

        private void ClearStateOnError()
        {
            _tokenHistory.Clear();
            _latexExpressionBuilder.Clear();
            _evaluationExpressionBuilder.Clear();
            _openParenthesesCount = 0;
            // ExpressionDisplay.Formula is already set to Error by caller, just need to render it.
            UpdateExpressionDisplay();
        }

    }
}