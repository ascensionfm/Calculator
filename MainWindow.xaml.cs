using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NCalc;
using Parlot.Fluent;
using System.Text.RegularExpressions;
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
        private void AppendToExpression(string displayContent, string evalContent, TokenType tokenType)
        {
            ResultDisplay.Formula = "";

            InputToken currentToken = new InputToken
            {
                Latex = displayContent,
                Eval = evalContent,
                Type = tokenType
            };
            if (currentToken.Type == TokenType.FunctionName)
            {
                currentToken.Latex += "(";
                currentToken.Eval += "(";
            }
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
                                                   currentToken.Type == TokenType.FunctionName);

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

            if (currentToken.Type == TokenType.FunctionName || currentToken.Type == TokenType.ParenOpen)
            {
                _openParenthesesCount++;
            }
            else if (currentToken.Type == TokenType.ParenClose)
            {
                if (_openParenthesesCount > 0) _openParenthesesCount--;
            }

            UpdateExpressionDisplay();
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
                case "%": displayOp = @" \% "; break;
                case "(": type = TokenType.ParenOpen; break;
                case ")": type = TokenType.ParenClose; break;
                case "Pow":
                    displayOp = @"\text{Pow}(";
                    evalOp = "Pow"; 
                    type = TokenType.FunctionName;
                    break;

            }
            AppendToExpression(displayOp, evalOp, type);
        }


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
                    if (token.Type == TokenType.FunctionName || token.Type == TokenType.ParenOpen)
                        _openParenthesesCount++;
                    else if (token.Type == TokenType.ParenClose && _openParenthesesCount > 0)
                        _openParenthesesCount--;
                }
            }
        }

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

                if (parenBalance < 0) break;
                if (i == 0 || (parenBalance == 0 && (token.Type == TokenType.Operator || token.Type == TokenType.FunctionName)))
                {
                    if (operandTokens.Count > 0 && (token.Type == TokenType.Operator || token.Type == TokenType.FunctionName) && i != _tokenHistory.Count - operandTokens.Count)
                    {
                        operandTokens.RemoveAt(0);
                    }
                    break;
                }
            }
            return operandTokens;
        }
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
                    AppendToExpression(@"\text{Pow}(", "Pow", TokenType.FunctionName);

                    foreach (var baseToken in baseTokens)
                    {
                        AppendToExpression(baseToken.Latex, baseToken.Eval, baseToken.Type);
                    }

                    AppendToExpression(",", ",", TokenType.Operator);
                    AppendToExpression("2", "2", TokenType.Number);
                    AppendToExpression(")", ")", TokenType.ParenClose);
                }
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
                default: evalFunc = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(funcLatex.Replace(@"\", "")); break;
            }
            AppendToExpression(funcLatex, evalFunc, TokenType.FunctionName);
        }

        private void ConstantButton_Click(object sender, RoutedEventArgs e)
        {
            string displayConst = ((Button)sender).Tag.ToString();
            string evalConst = "";
            if (displayConst == @"\pi") evalConst = "PI";
            else if (displayConst == "e") evalConst = "E";
            AppendToExpression(displayConst, evalConst, TokenType.Constant);
        }
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _latexExpressionBuilder.Clear();
            _evaluationExpressionBuilder.Clear();
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
        _latexExpressionBuilder.Clear();
        _evaluationExpressionBuilder.Clear();
        _openParenthesesCount = 0;
        foreach (InputToken token in _tokenHistory)
        {
            _latexExpressionBuilder.Append(token.Latex);
            _evaluationExpressionBuilder.Append(token.Eval);
            if (token.Type == TokenType.FunctionName || token.Type == TokenType.ParenOpen)
                _openParenthesesCount++;
            else if (token.Type == TokenType.ParenClose && _openParenthesesCount > 0)
                _openParenthesesCount--;
        }
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
            if (_evaluationExpressionBuilder.Length == 0) return;

            try
            {
                string finalEvalExpression = _evaluationExpressionBuilder.ToString();

                if (_openParenthesesCount > 0)
                {
                    string closingParentheses = new string(')', _openParenthesesCount);
                    finalEvalExpression += closingParentheses;
                }
                int lastPercentIdx = finalEvalExpression.LastIndexOf('%');
                if (lastPercentIdx > 0 && lastPercentIdx == finalEvalExpression.Length - 1)
                {
                    int operandStartIndex = lastPercentIdx - 1;
                    bool foundStart = false;
                    int parenBalance = 0;
                    for (int i = lastPercentIdx - 1; i >= 0; i--)
                    {
                        char c = finalEvalExpression[i];
                        if (c == ')') parenBalance++; else if (c == '(') parenBalance--;
                        if (parenBalance < 0) { operandStartIndex = i + 1; foundStart = true; break; }
                        if (parenBalance == 0 && ("+-*/^(".Contains(c) || i == 0))
                        {
                            operandStartIndex = (i == 0 && !"+-*/^(".Contains(c)) ? 0 : i + 1;
                            foundStart = true; break;
                        }
                    }
                    if (!foundStart && operandStartIndex < 0) operandStartIndex = 0;
                    string operand = finalEvalExpression.Substring(operandStartIndex, lastPercentIdx - operandStartIndex);
                    if (!string.IsNullOrWhiteSpace(operand))
                    {
                        string prefix = finalEvalExpression.Substring(0, operandStartIndex);
                        finalEvalExpression = $"{prefix}(({operand})/100.0)";
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
                            if (!_isRadians)
                            {
                                val = val * (Math.PI / 180.0);
                            }
                            if (name.Equals("Sin", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Sin(val);
                            else if (name.Equals("Cos", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Cos(val);
                            else if (name.Equals("Tan", StringComparison.OrdinalIgnoreCase)) args.Result = Math.Tan(val);
                        }
                    }
                };

                object result = expr.Evaluate();
                // --- START DEBUGGING SECTION ---
                MessageBox.Show($"NCalc Evaluate() for '{finalEvalExpression}' returned: {result?.ToString()} (Type: {result?.GetType()?.ToString()})", "NCalc Raw Result");
                // --- END DEBUGGING SECTION ---

                string resultString;
                if (result is double d)
                // ... (rest of your result formatting logic) ...
                {
                    if (Math.Abs(d) > 1e12 || (Math.Abs(d) < 1e-9 && d != 0))
                    {
                        resultString = d.ToString("E6", CultureInfo.InvariantCulture);
                    }
                    else if (d == Math.Truncate(d))
                    {
                        resultString = Convert.ToInt64(d).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        resultString = d.ToString("G10", CultureInfo.InvariantCulture);
                        if (resultString.Contains("."))
                        {
                            resultString = resultString.TrimEnd('0').TrimEnd(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator[0]);
                        }
                        if (string.IsNullOrEmpty(resultString) || resultString == "-0" || resultString == "-") resultString = "0";
                        else if (resultString == CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator[0].ToString()) resultString = "0";
                    }
                }
                else
                {
                    resultString = result?.ToString() ?? "Error";
                }

                ResultDisplay.Formula = resultString.Replace(",", ".");
                _tokenHistory.Clear();
                _latexExpressionBuilder.Clear().Append(resultString.Replace(",", "."));
                _evaluationExpressionBuilder.Clear().Append(resultString.Replace(",", "."));
                _openParenthesesCount = 0;
            }
            catch (Exception ex)
            {
                ResultDisplay.Formula = @"\text{Error}";
                _tokenHistory.Clear();
            }
            UpdateExpressionDisplay();
        }
    }
}