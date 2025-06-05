using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NCalc;
using System.Text.RegularExpressions; // For percentage, if used

namespace WpfLatexCalculator // Assuming this is your namespace
{
    public partial class MainWindow : Window
    {
        private StringBuilder _latexExpressionBuilder = new StringBuilder();
        private StringBuilder _evaluationExpressionBuilder = new StringBuilder();
        private List<InputToken> _tokenHistory = new List<InputToken>();
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
            PostfixValueOp // If you have operations like factorial or percent as postfix
        }

        private class InputToken
        {
            public string Latex { get; set; }
            public string Eval { get; set; }
            public TokenType Type { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            AngleModeButton.Content = DegSymbol; // Ensure AngleModeButton is named in your XAML
            UpdateExpressionDisplay();
        }

        private void UpdateExpressionDisplay()
        {
            // Ensure ExpressionDisplay is a FormulaControl or similar in your XAML
            ExpressionDisplay.Formula = _latexExpressionBuilder.ToString();
        }

        // Core method: Based on original user upload's logic for FunctionName and _openParenthesesCount
        private void AppendToExpression(string displayContent, string evalContent, TokenType tokenType)
        {
            ResultDisplay.Formula = ""; // Clear previous result display

            InputToken currentToken = new InputToken
            {
                Latex = displayContent,
                Eval = evalContent,
                Type = tokenType
            };

            // Implicit multiplication logic (from original user upload)
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
                                                   currentToken.Type == TokenType.FunctionName); // Functions start values

                if (lastProducesValue && currentStartsValueAndIsNotBinaryOperator)
                {
                    bool extendingNumber = (currentToken.Type == TokenType.Number && lastToken.Type == TokenType.Number && !currentToken.Eval.StartsWith(".")) ||
                                           (currentToken.Eval == "." && lastToken.Type == TokenType.Number && !lastToken.Eval.Contains("."));

                    if (!extendingNumber)
                    {
                        InputToken multiplyToken = new InputToken { Latex = @" \times ", Eval = "*", Type = TokenType.Operator };
                        _tokenHistory.Add(multiplyToken);
                        _latexExpressionBuilder.Append(multiplyToken.Latex);
                        _evaluationExpressionBuilder.Append(multiplyToken.Eval);
                    }
                }
            }

            // Add "0" before a decimal point if it's the start of a number or after an operator
            if (currentToken.Type == TokenType.Number && currentToken.Eval == ".")
            {
                if (!_tokenHistory.Any() ||
                    (_tokenHistory.Last().Type != TokenType.Number &&
                     _tokenHistory.Last().Type != TokenType.ParenClose && // e.g. (0.5) not ((.5)
                     _tokenHistory.Last().Type != TokenType.Constant &&
                     _tokenHistory.Last().Type != TokenType.PostfixValueOp)
                    )
                {
                    InputToken zeroToken = new InputToken { Latex = "0", Eval = "0", Type = TokenType.Number };
                    _tokenHistory.Add(zeroToken);
                    _latexExpressionBuilder.Append(zeroToken.Latex);
                    _evaluationExpressionBuilder.Append(zeroToken.Eval);
                }
                // Prevent multiple decimal points in the current number segment
                string currentNumberEvalSegment = "";
                for (int i = _tokenHistory.Count - 1; i >= 0; i--)
                {
                    var pt = _tokenHistory[i];
                    if (pt.Type != TokenType.Number && pt.Eval != ".") break;
                    currentNumberEvalSegment = pt.Eval + currentNumberEvalSegment;
                }
                if (currentNumberEvalSegment.Contains(".")) return; // Already has a decimal
            }


            // CRITICAL: Original FunctionName logic from user's file
            if (currentToken.Type == TokenType.FunctionName)
            {
                currentToken.Latex += "(";
                currentToken.Eval += "(";
            }

            _tokenHistory.Add(currentToken);
            _latexExpressionBuilder.Append(currentToken.Latex);
            _evaluationExpressionBuilder.Append(currentToken.Eval);

            // CRITICAL: Original _openParenthesesCount logic from user's file
            if (currentToken.Type == TokenType.FunctionName || currentToken.Type == TokenType.ParenOpen)
            {
                _openParenthesesCount++;
            }
            else if (currentToken.Type == TokenType.ParenClose)
            {
                if (_openParenthesesCount > 0) _openParenthesesCount--;
                else { /* Mismatched closing parenthesis, might ignore or handle as error */ }
            }
            UpdateExpressionDisplay();
        }

        // Core method: Based on original user upload's logic for _openParenthesesCount
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
                    // CRITICAL: Original _openParenthesesCount logic from user's file
                    if (token.Type == TokenType.FunctionName || token.Type == TokenType.ParenOpen)
                        _openParenthesesCount++;
                    else if (token.Type == TokenType.ParenClose && _openParenthesesCount > 0)
                        _openParenthesesCount--;
                }
            }
        }

        // Refined method to extract the base for power operations
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
                // FunctionNames (like "sin", "Pow") effectively open a scope for evaluation
                else if (token.Type == TokenType.FunctionName) parenBalance--;


                if (parenBalance < 0) break;

                if (i == 0 || (parenBalance == 0 &&
                    (token.Type == TokenType.Operator && !("()".Contains(token.Eval) || string.IsNullOrEmpty(token.Eval))) || // Non-paren binary operators
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

        private void NumberButton_Click(object sender, RoutedEventArgs e)
        {
            string number = ((Button)sender).Tag.ToString();
            AppendToExpression(number, number, TokenType.Number);
        }

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
                case "%": displayOp = @" \% "; evalOp = "%"; break; // NCalc doesn't directly support %, handled in Equals
                case "(": type = TokenType.ParenOpen; break;
                case ")": type = TokenType.ParenClose; break;

                case "Pow": // For a button explicitly labeled "Pow" or similar, for Pow(base,exponent) input
                            // Results in LaTeX: \text{Pow}( and Eval: Pow(
                    AppendToExpression(@"\text{Pow}", "Pow", TokenType.FunctionName);
                    return;

                case "^": // For x^y functionality (e.g., a button labeled "xʸ" with Tag="^")
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
                        AppendToExpression("", "Pow", TokenType.FunctionName); // LaTeX: '(', Eval: 'Pow('
                        foreach (var token in baseTokens)
                        {
                            AppendToExpression(token.Latex, token.Eval, token.Type); // Appends base
                        }
                        AppendToExpression("^{", ",", TokenType.Operator); // LaTeX: '(base)^{', Eval: 'Pow(base,'
                    }
                    else { ResultDisplay.Formula = @"\text{Error: Base required for x}^y"; }
                    return;
            }
            AppendToExpression(displayOp, evalOp, type);
        }

        private void PowerButton_Click(object sender, RoutedEventArgs e) // For x² (e.g., Tag="^2")
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

                    AppendToExpression("", "Pow", TokenType.FunctionName); // LaTeX: '(', Eval: 'Pow('
                    foreach (var baseToken in baseTokens)
                    {
                        AppendToExpression(baseToken.Latex, baseToken.Eval, baseToken.Type);
                    }
                    AppendToExpression(")^{", ",", TokenType.Operator);   // LaTeX: '(base)^{', Eval: 'Pow(base,'
                    AppendToExpression("2", "2", TokenType.Number);      // LaTeX: '(base)^{2', Eval: 'Pow(base,2'
                    AppendToExpression("}", ")", TokenType.ParenClose);   // LaTeX: '(base)^{2}', Eval: 'Pow(base,2)'
                }
                else { ResultDisplay.Formula = @"\text{Error: Base required for x}^2"; }
                // UpdateExpressionDisplay() is called by the last AppendToExpression
            }
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
                default: // Fallback for other functions
                    evalFunc = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(funcLatex.Replace(@"\", "")).Replace(" ", "");
                    break;
            }
            AppendToExpression(funcLatex, evalFunc, TokenType.FunctionName); // AppendToExpression adds "("
        }

        private void ConstantButton_Click(object sender, RoutedEventArgs e)
        {
            string displayConst = ((Button)sender).Tag.ToString();
            string evalConst = "";
            if (displayConst == @"\pi") evalConst = "PI";
            else if (displayConst == "e") evalConst = "E";
            else evalConst = displayConst; // Allow other constants by Tag
            AppendToExpression(displayConst, evalConst, TokenType.Constant);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) // 'C' button
        {
            _latexExpressionBuilder.Clear();
            _evaluationExpressionBuilder.Clear();
            _tokenHistory.Clear();
            ResultDisplay.Formula = "";
            _openParenthesesCount = 0;
            UpdateExpressionDisplay();
        }

        private void BackspaceButton_Click(object sender, RoutedEventArgs e) // Backspace/delete last token
        {
            if (_tokenHistory.Any())
            {
                _tokenHistory.RemoveAt(_tokenHistory.Count - 1);
                RebuildBuildersFromHistory(updateOpenParenCount: true); // Recalculate everything
                ResultDisplay.Formula = ""; // Clear previous result as expression changed
                UpdateExpressionDisplay();
            }
        }

        private void ModeButton_Click(object sender, RoutedEventArgs e) // Deg/Rad toggle
        {
            _isRadians = !_isRadians;
            AngleModeButton.Content = _isRadians ? RadSymbol : DegSymbol;
            ResultDisplay.Formula = ""; // Clear result as mode change invalidates it
        }

        private void ClearStateOnError()
        {
            _tokenHistory.Clear();
            _latexExpressionBuilder.Clear();
            _evaluationExpressionBuilder.Clear();
            _openParenthesesCount = 0;
            // ExpressionDisplay.Formula should have been set to Error by the caller
            UpdateExpressionDisplay(); // Render the error state in main display
        }

        private void EqualsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_evaluationExpressionBuilder.Length == 0 && _tokenHistory.Any())
            {
                RebuildBuildersFromHistory(true); // Try to build from history if eval string is empty
            }

            // If still no valid expression to evaluate (e.g., only operators, or empty)
            if (_evaluationExpressionBuilder.Length == 0 ||
                (_tokenHistory.Count > 0 && _tokenHistory.All(t => t.Type == TokenType.Operator && !("()".Contains(t.Eval)))))
            {
                // If only a number was entered, display it as the "result" without error
                if (_tokenHistory.Count > 0 && _tokenHistory.All(t => t.Type == TokenType.Number || t.Eval == "."))
                {
                    ResultDisplay.Formula = _latexExpressionBuilder.ToString();
                    // ExpressionDisplay already shows this number.
                }
                else if (_tokenHistory.Count == 0)
                {
                    ResultDisplay.Formula = ""; // Truly empty
                }
                else
                { // Invalid expression
                    ResultDisplay.Formula = @"\text{Error}";
                    ExpressionDisplay.Formula = @"\text{Error}";
                    ClearStateOnError();
                }
                return;
            }

            try
            {
                string finalEvalExpression = _evaluationExpressionBuilder.ToString();

                if (_openParenthesesCount > 0)
                {
                    finalEvalExpression += new string(')', _openParenthesesCount);
                }

                // Percentage handling: NCalc doesn't have '%', so convert 'X%' to '(X/100.0)'
                // This is a simplified version; robust parsing is complex.
                while (finalEvalExpression.Contains("%"))
                {
                    int idx = finalEvalExpression.LastIndexOf('%');
                    if (idx > 0)
                    {
                        // Try to find the operand before '%'
                        // This is a very basic attempt and might not cover all edge cases (e.g. complex expressions before %)
                        string tempExpression = finalEvalExpression.Substring(0, idx);
                        Match m = Regex.Match(tempExpression, @"(\([^\(\)]+\)|[0-9\.]+)$"); // Matches (expression) or number
                        if (m.Success && m.Index + m.Length == tempExpression.Length)
                        {
                            string operand = m.Value;
                            finalEvalExpression = tempExpression.Substring(0, m.Index) + $"(({operand})/100.0)" + finalEvalExpression.Substring(idx + 1);
                        }
                        else
                        {
                            finalEvalExpression = finalEvalExpression.Remove(idx, 1); // Fallback: remove '%' if operand not simple
                        }
                    }
                    else
                    {
                        finalEvalExpression = finalEvalExpression.Remove(idx, 1); // Fallback: remove '%' if at start
                    }
                }

                NCalc.Expression expr = new NCalc.Expression(finalEvalExpression);
                expr.Parameters["PI"] = Math.PI;
                expr.Parameters["E"] = Math.E;

                expr.EvaluateFunction += (name, args) =>
                {
                    if (name.Equals("Sin", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Cos", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Tan", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Parameters.Length == 1)
                        {
                            double val = Convert.ToDouble(args.Parameters[0].Evaluate());
                            if (!_isRadians) val *= (Math.PI / 180.0);

                            if (name.Equals("Sin", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Sin(val);
                            else if (name.Equals("Cos", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Cos(val);
                            else if (name.Equals("Tan", StringComparison.OrdinalIgnoreCase))
                            {
                                if (Math.Abs(Math.Cos(val)) < 1E-12) throw new DivideByZeroException("Tangent undefined.");
                                args.Result = Math.Tan(val);
                            }
                        }
                        else throw new ArgumentException("Trig functions expect 1 argument.");
                    }
                    // NCalc handles Pow(base, exponent) natively.
                };

                object result = expr.Evaluate();
                string resultString;

                if (result is double d)
                {
                    if (double.IsNaN(d) || double.IsInfinity(d)) { resultString = @"\text{Error}"; }
                    else if (Math.Abs(d) > 1E12 || (Math.Abs(d) < 1E-9 && d != 0)) { resultString = d.ToString("E6", CultureInfo.InvariantCulture); }
                    else
                    {
                        resultString = d.ToString("G15", CultureInfo.InvariantCulture); // Use G15 for more precision
                        if (resultString.Contains(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator))
                        {
                            resultString = resultString.TrimEnd('0').TrimEnd(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator[0]);
                        }
                        if (string.IsNullOrEmpty(resultString) || resultString == "-0" || resultString == CultureInfo.InvariantCulture.NumberFormat.NegativeSign) resultString = "0";
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
                    UpdateExpressionDisplay(); 
                }
            }
            catch (DivideByZeroException dbzEx)
            {
                ResultDisplay.Formula = @"\text{Error: Div by Zero}";
                ExpressionDisplay.Formula = @"\text{Error}";
                ClearStateOnError();
            }
            catch (Exception ex)
            { // Catch other NCalc or general errors
                ResultDisplay.Formula = @"\text{Error}";
                ExpressionDisplay.Formula = @"\text{Error}";
                // For debugging: System.Windows.MessageBox.Show(ex.ToString(), "Calculation Error");
                ClearStateOnError();
            }
        }
    }
}