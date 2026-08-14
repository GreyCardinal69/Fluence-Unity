using Fluence.Unity.Exceptions;
using System.Runtime.CompilerServices;
using System.Text;
using static Fluence.Unity.FluenceInterpreter;
using static Fluence.Unity.Token;

namespace Fluence.Unity
{
    /// <summary>
    /// The lexical analyzer for Fluence.
    /// It scans the source code string and converts it into a stream of tokens.
    /// </summary>
    internal sealed class FluenceLexer
    {
        private string _sourceCode;
        private int _sourceLength;
        private int _currentPosition;
        private int _currentLine;
        private int _currentColumn;
        private int _currentLineBeforeWhiteSpace;
        private int _currentColumnBeforeWhiteSpace;
        private readonly TokenBuffer _tokenBuffer;
        private readonly string _fileName;
        private readonly StringBuilder _sb = new StringBuilder();

        private bool _hasReachedEndInternal => _currentPosition >= _sourceLength;

        internal bool HasReachedEnd => _currentPosition >= _sourceLength & _tokenBuffer.HasReachedEnd;
        internal int TokenCount => _tokenBuffer.TokenCount;
        internal string SourceCode => _sourceCode;

        public FluenceLexer()
        {
            _tokenBuffer = new TokenBuffer(this, 0);
        }

        internal FluenceLexer(string source, string fileName = null!)
        {
            _sourceCode = source;
            _sourceLength = source.Length;
            _currentPosition = 0;
            _currentLine = 1;
            _currentColumn = 1;
            _fileName = fileName;
            _tokenBuffer = new TokenBuffer(this, _sourceLength / 4);
        }

        internal FluenceLexer(List<Token> stream, string fileName = null!)
        {
            _currentPosition = 0;
            _currentLine = 1;
            _currentColumn = 1;
            _fileName = fileName;
            _tokenBuffer = new TokenBuffer(this, 0);
            _tokenBuffer.SetTokens(stream);
        }

        internal FluenceLexer(List<Token> tokens)
        {
            _tokenBuffer = new TokenBuffer(this, 0);
            _tokenBuffer.AddRange(tokens);
        }

        private sealed class TokenBuffer
        {
            private List<Token> _buffer;
            private readonly FluenceLexer _lexer;
            private int _head;
            private bool _lexerFinished;

            internal int TokenCount => _buffer.Count;

            internal bool HasReachedEnd
            {
                get
                {
                    if (!_lexerFinished)
                    {
                        EnsureFilled(1);
                    }

                    if (_head >= _buffer.Count)
                    {
                        return _lexerFinished;
                    }

                    return _buffer[_head].Type == TokenType.EOF;
                }
            }

            internal TokenBuffer(FluenceLexer lexer, int estimatedTokenCount)
            {
                _buffer = new List<Token>(estimatedTokenCount);
                _lexer = lexer;
            }

            internal void Reset()
            {
                _buffer.Clear();
                _head = 0;
                _lexerFinished = false;
            }

            /// <summary>
            /// Populates the current token bugger from a given token stream.
            /// </summary>
            /// <param name="tokens">The token stream to populate the buffer with.</param>
            internal void SetTokens(List<Token> tokens) => _buffer = tokens;

            /// <summary>
            /// Returns all the currently parsed tokens.
            /// </summary>
            internal List<Token> AllTokens() => _buffer;

            /// <summary>
            /// Consumes the next token from the buffer and advances the head.
            /// </summary>
            internal Token Consume()
            {
                EnsureFilled(1);

                Token token = _buffer[_head];

                if (token.Type != TokenType.EOF)
                {
                    _head++;
                }

                return token;
            }

            internal void ModifyTokenAt(int index, Token newToken) => _buffer[index] = newToken;

            /// <summary>
            /// Erases a sequence of tokens from the stream by overwriting them with End-Of-Line (<see cref="TokenType.EOL"/>) tokens.
            /// </summary>
            /// <remarks>
            /// This is used heavily during the parser's pre-pass phase to remove declarations (like structs and enums) 
            /// after their symbols have been built. Overwriting with EOL is a high-performance, zero-allocation alternative 
            /// to removing items from the list, as the main parser pass naturally ignores trailing semicolons/EOLs.
            /// </remarks>
            /// <param name="startIndex">The zero-based index of the first token to erase.</param>
            /// <param name="endIndex">The zero-based index of the last token to erase (inclusive).</param>
            internal void EraseTokenRange(int startIndex, int endIndex)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    ModifyTokenAt(i, Token.EOL);
                }
            }

            internal bool TokenTypeMatches(TokenType type)
            {
                EnsureFilled(1);
                return _buffer[_head].Type == type;
            }

            internal void AddRange(List<Token> tokens) => _buffer.AddRange(tokens);

            internal void ClearTokens() => _buffer.Clear();

            /// <summary>
            /// Peeks ahead a given number of tokens from the current position.
            /// lookahead=1 is the very next token.
            /// </summary>
            internal Token Peek(int lookahead = 1)
            {
                EnsureFilled(lookahead != 0 ? lookahead : 1);

                int index = _head + lookahead - (lookahead == 0 ? 0 : 1);
                if (index >= _buffer.Count)
                {
                    return _buffer[^1];
                }

                return _buffer[index];
            }

            /// <summary>
            /// Advances the buffer's head by a specified number of tokens without returning them.
            /// This is more efficient than calling Consume() when the token's value is not needed.
            /// </summary>
            internal void Advance()
            {
                EnsureFilled(1);

                if (_head + 1 <= _buffer.Count)
                {
                    _head += 1;
                    return;
                }

                _head = _buffer.Count;
            }

            /// <summary>
            /// Inserts a new token at the current position in the buffer.
            /// </summary>
            /// <param name="type">The type of the token.</param>
            internal void InsertNextToken(TokenType type)
            {
                EnsureFilled(1);
                _buffer.Insert(_head, new Token(type));
            }

            internal TokenType PeekNextTokenType()
            {
                EnsureFilled(1);

                if (_head >= _buffer.Count)
                {
                    return _buffer[^1].Type;
                }

                return _buffer[_head].Type;
            }

            internal TokenType PeekTokenTypeAheadByN(int n)
            {
                EnsureFilled(n);

                int index = _head + n - 1;

                if (index >= _buffer.Count)
                {
                    return _buffer[^1].Type;
                }

                return _buffer[index].Type;
            }

            private void EnsureFilled(int requiredCount)
            {
                // If the lexer is already known to be finished, we cannot generate more tokens.
                if (_lexerFinished)
                {
                    return;
                }

                while ((_buffer.Count - _head) < requiredCount)
                {
                    Token nextToken = _lexer.GetNextToken();

                    if (nextToken.Type == TokenType.NEW_LINE)
                    {
                        continue;
                    }

                    _buffer.Add(nextToken);

                    if (nextToken.Type == TokenType.EOF)
                    {
                        _lexerFinished = true;
                        break;
                    }
                }
            }

            /// <summary>
            /// Inserts a range of <see cref="Token"/>s at the very end of the token stream just before the <see cref="EOF"/> token.
            /// </summary>
            /// <param name="newTokens">The range of tokens to inser.</param>
            internal void InsertBeforeEOF(List<Token> tokens)
            {
                if (_buffer.Count > 0 && _buffer[^1].Type == TokenType.EOF)
                {
                    _buffer.InsertRange(_buffer.Count - 1, tokens);
                    return;
                }

                _buffer.AddRange(tokens);
            }
        }

        internal void Reset()
        {
            _tokenBuffer.Reset();
            _currentLine = 0;
            _currentColumn = 0;
            _currentColumnBeforeWhiteSpace = 0;
            _currentLineBeforeWhiteSpace = 0;
            _currentPosition = 0;
        }

        internal void Initialize(string source)
        {
            _sourceCode = source;
            _sourceLength = source.Length;
            _currentPosition = 0;
            _currentLine = 1;
            _currentColumn = 1;
        }

        /// <summary>
        /// Inserts a range of <see cref="Token"/>s at the very end of the token stream just before the <see cref="EOF"/> token.
        /// </summary>
        /// <param name="newTokens">The range of tokens to inser.</param>
        internal void InsertBeforeEOF(List<Token> newTokens) => _tokenBuffer.InsertBeforeEOF(newTokens);

        internal void Initialize(List<Token> tokens) => _tokenBuffer.AddRange(tokens);

        /// <summary>
        /// Peeks at the next token in the stream without consuming it.
        /// </summary>
        internal Token PeekNextToken() => _tokenBuffer.Peek();

        /// <summary>
        /// Peeks at the current token in the stream without consuming it.
        /// </summary>
        internal Token PeekCurrentToken() => _tokenBuffer.Peek(0);

        /// <summary>
        /// Advances the token stream by one position without returning the consumed token.
        /// </summary>
        internal void Advance() => _tokenBuffer.Advance();

        internal void ClearTokens() => _tokenBuffer.ClearTokens();

        /// <summary>
        /// Returns all the currently parsed tokens.
        /// </summary>
        internal List<Token> AllTokens() => _tokenBuffer.AllTokens();

        /// <summary>
        /// Advances the token stream by a given amount of positions without returning the consumed token. Advances by 1 by default.
        /// </summary>
        internal void AdvanceMany(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                _tokenBuffer.Advance();
            }
        }

        /// <summary>
        /// Replaces the token in the token stream at the given index. Unsafe.
        /// </summary>
        internal void ModifyTokenAt(int index, Token newToken) => _tokenBuffer.ModifyTokenAt(index, newToken);

        /// <summary>
        /// Peeks N tokens ahead in the stream without consuming them.
        /// </summary>
        internal Token PeekAheadByN(int n) => _tokenBuffer.Peek(n);

        /// <summary>
        /// Inserts a new token at the current position in the buffer.
        /// </summary>
        /// <param name="type">The type of the token</param>
        internal void InsertNextToken(TokenType type) => _tokenBuffer.InsertNextToken(type);

        /// <summary>
        /// Peeks ahead by a given amount and returns the <see cref="TokenType"/> of the token at that position.
        /// </summary>
        /// <param name="n">The count to peek ahead.</param>
        /// <returns>The <see cref="TokenType"/> at the position.</returns>
        internal TokenType PeekTokenTypeAheadByN(int n) => _tokenBuffer.PeekTokenTypeAheadByN(n);

        /// <summary>
        /// Consumes and returns the next token from the stream.
        /// </summary>
        internal Token ConsumeToken() => _tokenBuffer.Consume();

        /// <summary>
        /// Checks if the provided <see cref="TokenType"/> matches the next token's type in the buffer.
        /// </summary>
        /// <param name="type">The token type to compare with.</param>
        /// <returns>True if the next token is of the same type.</returns>
        internal bool TokenTypeMatches(TokenType type) => _tokenBuffer.TokenTypeMatches(type);

        /// <summary>
        /// Returns the <see cref="TokenType"/> of the next token in the buffer.
        /// </summary>
        internal TokenType PeekNextTokenType() => _tokenBuffer.PeekNextTokenType();

        /// <summary>
        /// Erases a sequence of tokens from the stream by overwriting them with End-Of-Line (<see cref="TokenType.EOL"/>) tokens.
        /// </summary>
        /// <remarks>
        /// This is used heavily during the parser's pre-pass phase to remove declarations (like structs and enums) 
        /// after their symbols have been built. Overwriting with EOL is a high-performance, zero-allocation alternative 
        /// to removing items from the list, as the main parser pass naturally ignores trailing semicolons/EOLs.
        /// </remarks>
        /// <param name="startIndex">The zero-based index of the first token to erase.</param>
        /// <param name="endIndex">The zero-based index of the last token to erase (inclusive).</param>
        internal void EraseTokenRange(int startIndex, int endIndex) => _tokenBuffer.EraseTokenRange(startIndex, endIndex);

        internal void DumpTokenStream(string title, TextOutputMethod outMethod)
        {
            outMethod(string.Empty);
            outMethod($"--- {title} ---");
            outMethod(string.Empty);

            string header = string.Format("{0,-35} {1,-40} {2,-30} {3, -10} {4, -10}", "TYPE", "TEXT", "LITERAL", "LINE", "COLUMN");
            outMethod(header);
            outMethod(new string('-', header.Length));

            int lookahead = 1;
            while (true)
            {
                Token token = PeekAheadByN(lookahead);

                // Stop when we hit the end of the file.
                if (token.Type == TokenType.EOF)
                {
                    outMethod(string.Format("{0,-35} {1,-40} {2,-30} {3, -10} {4, -10}", token.Type, "EOF", "EOF", "EOF", "EOF"));
                    break;
                }

                string textToDisplay = (token.Text ?? "null")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");

                string literalToDisplay = token.Literal?.ToString() ?? "null";
                literalToDisplay = (literalToDisplay is "\r\n" or "\n") ? "NewLine" : literalToDisplay;

                if (textToDisplay.Length > 20)
                {
                    textToDisplay = $"{textToDisplay[..20]}...";
                }

                if (literalToDisplay.Length > 20)
                {
                    literalToDisplay = $"{literalToDisplay[..20]}...".Replace("\n", "\\n");
                }

                outMethod(string.Format("{0,-35} {1,-40} {2,-30} {3, -10} {4, -10}",
                    token.Type,
                    textToDisplay,
                    literalToDisplay,
                    token.LineInSourceCode,
                    token.ColumnInSourceCode));

                lookahead++;
            }

            outMethod(string.Empty);
            outMethod($"--- End of {title} ---");
            outMethod(string.Empty);
        }

        /// <summary>
        /// Forces the entire source file to be tokenized immediately.
        /// </summary>
        internal void LexFullSource()
        {
            int lookAhead = 1;
            while (!_hasReachedEndInternal)
            {
                _tokenBuffer.Peek(lookAhead);
                lookAhead++;
            }
        }

        /// <summary>
        /// Scans and returns the next logical token from the source code.
        /// </summary>
        private Token GetNextToken()
        {
            _currentColumnBeforeWhiteSpace = _currentColumn;
            _currentLineBeforeWhiteSpace = _currentLine;

            if (_hasReachedEndInternal) return EOF;

            SkipWhiteSpaceAndComments();

            if (_hasReachedEndInternal) return EOF;

            // A conditional '#IF' block of code.
            if (_sourceCode[_currentPosition] == '#' && CanLookAheadStartInclusive(3) && PeekString(3).SequenceEqual("#IF"))
            {
                _currentLineBeforeWhiteSpace = _currentLine;
                _currentColumnBeforeWhiteSpace = _currentColumn;
                return MakeTokenAndTryAdvance(TokenType.CONDITIONAL_IF, 3);
            }

            char currChar = _sourceCode[_currentPosition];
            int startPos = _currentPosition;

            // Line of code where an error has been detected.
            string faultyCodeLine;

            /* The operator suite is quite large.
             * 1 Char: + - * / % < > = ! ^ ~ | &
             * 2 Char: == != <= => || && ** is >> << |> |? <| ~> .. ++ --
             * +=. -=. *=, /=, or
             * 3 Char: |?? |>> |~> <<| <>| <n| <?| not, <~| ->>
             * 4 Char: |>>= <==| <!=| <<=| <>=| <??| <n|?, <~?| <N!|
             * 5 Char: <||<| <||>| <N!?|
             * 6 Char: <||!=| <||==| <||??|
             * And more.
             */

            /* Starting With _char_
             * <        <, <=, <<, <|, <<|, <>|, <n|, <?|, <==|, <!=|, <<=|, <>=|, <??|, <n|? and all of 6 Char
             * |        |>>=, |??, |>>, |~>, ||, |>, |?, |
             */

            /* For others we have:
             * [,], {,}, (,)
             * . , ; :
             * Keywords
             * .. for ranges
             * =>, -> for functions and lambdas
             * Literals
             * _ for pipes
             * EOL
             */
            char nextChar;

            switch (currChar)
            {
                case '<': return ScanLessThanOperator();
                case '|': return ScanPipe();
                case '+':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '+') return MakeTokenAndTryAdvance(TokenType.INCREMENT, 2);
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_PLUS, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.PLUS, 1);
                case '-':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '-') return MakeTokenAndTryAdvance(TokenType.DECREMENT, 2);
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_MINUS, 2);
                        if (nextChar == '>') return MakeTokenAndTryAdvance(TokenType.THIN_ARROW, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.MINUS, 1);
                case '/':
                    if (CanLookAheadStartInclusive(2) && PeekNext() == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_DIV, 2);
                    return MakeTokenAndTryAdvance(TokenType.SLASH, 1);
                case '%':
                    if (CanLookAheadStartInclusive(2) && PeekNext() == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_PERCENT, 2);
                    return MakeTokenAndTryAdvance(TokenType.PERCENT, 1);
                case '^': return MakeTokenAndTryAdvance(TokenType.CARET, 1);
                case '*':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '*') return MakeTokenAndTryAdvance(TokenType.EXPONENT, 2);
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_MUL, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.STAR, 1);
                case '&':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '&') return MakeTokenAndTryAdvance(TokenType.AND, 2);
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_AMPERSAND, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.AMPERSAND, 1);
                case '>':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '<') return MakeTokenAndTryAdvance(TokenType.SWAP, 2);
                        if (nextChar == '>') return MakeTokenAndTryAdvance(TokenType.BITWISE_RIGHT_SHIFT, 2);
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.GREATER_EQUAL, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.GREATER, 1);
                case '~': return MakeTokenAndTryAdvance(TokenType.TILDE, 1);
                case '!':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '!') return MakeTokenAndTryAdvance(TokenType.BOOLEAN_FLIP, 2);
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.BANG_EQUAL, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.BANG, 1);
                case '=':
                    if (CanLookAheadStartInclusive(2))
                    {
                        nextChar = PeekNext();
                        if (nextChar == '=') return MakeTokenAndTryAdvance(TokenType.EQUAL_EQUAL, 2);
                        if (nextChar == '>') return MakeTokenAndTryAdvance(TokenType.ARROW, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.EQUAL, 1);
                case '[': return MakeTokenAndTryAdvance(TokenType.L_BRACKET, 1);
                case ']': return MakeTokenAndTryAdvance(TokenType.R_BRACKET, 1);
                case '{': return MakeTokenAndTryAdvance(TokenType.L_BRACE, 1);
                case '}': return MakeTokenAndTryAdvance(TokenType.R_BRACE, 1);
                case '(': return MakeTokenAndTryAdvance(TokenType.L_PAREN, 1);
                case ')': return MakeTokenAndTryAdvance(TokenType.R_PAREN, 1);
                case ';': return MakeTokenAndTryAdvance(TokenType.EOL, 1);
                case ',': return MakeTokenAndTryAdvance(TokenType.COMMA, 1);
                case '?':
                    if (CanLookAheadStartInclusive(3))
                    {
                        if (PeekString(3).SequenceEqual("??=")) return MakeTokenAndTryAdvance(TokenType.NULL_COALESCING_ASSIGN, 3);
                    }
                    if (CanLookAheadStartInclusive(2))
                    {
                        if (PeekString(2).SequenceEqual("??")) return MakeTokenAndTryAdvance(TokenType.NULL_COALESCING, 2);
                        if (PeekString(2).SequenceEqual("?:")) return MakeTokenAndTryAdvance(TokenType.TERNARY_JOINT, 2);
                        if (PeekString(2).SequenceEqual("?.")) return MakeTokenAndTryAdvance(TokenType.NULL_COND, 2);
                    }
                    return MakeTokenAndTryAdvance(TokenType.QUESTION, 1);
                case ':': return MakeTokenAndTryAdvance(TokenType.COLON, 1);
                case '\'':
                    _currentPosition++;
                    return MakeTokenAndTryAdvance(TokenType.CHARACTER, 2, null!, _sourceCode[_currentPosition]);
                case '\r':
                case '\n':
                    // If it's a newline, we need to update our position trackers.
                    if (currChar == '\r')
                    {
                        if (CanLookAheadStartInclusive(2) && _sourceCode[_currentPosition + 1] == '\n')
                        {
                            AdvancePosition(); // Consume the '\n' as part of the '\r\n' pair.
                        }
                    }
                    AdvanceCurrentLine();
                    AdvancePosition();
                    return NEW_LINE;
            }

            // Other cases done individually.

            bool currentIsADot = currChar is '.';

            if (currentIsADot && !IsNumeric(PeekNext()))
            {
                if (CanLookAheadStartInclusive(4))
                {
                    // .++ and .-- also fit here.
                    // Since they require parentheses.
                    // So we have .++() or .--();

                    ReadOnlySpan<char> peek = PeekString(4);

                    if (peek.SequenceEqual(".and"))
                    {
                        return MakeTokenAndTryAdvance(TokenType.DOT_AND_CHECK, 4);
                    }

                    ReadOnlySpan<char> peek3 = peek[..3];

                    string slice3 = peek3.ToString();
                    switch (slice3)
                    {
                        case ".or": return MakeTokenAndTryAdvance(TokenType.DOT_OR_CHECK, 3);
                        case ".++": return MakeTokenAndTryAdvance(TokenType.DOT_INCREMENT, 3);
                        case ".--": return MakeTokenAndTryAdvance(TokenType.DOT_DECREMENT, 3);
                        case ".-=": return MakeTokenAndTryAdvance(TokenType.DOT_MINUS_EQUAL, 3);
                        case ".+=": return MakeTokenAndTryAdvance(TokenType.DOT_PLUS_EQUAL, 3);
                        case "./=": return MakeTokenAndTryAdvance(TokenType.DOT_SLASH_EQUAL, 3);
                        case ".*=": return MakeTokenAndTryAdvance(TokenType.DOT_STAR_EQUAL, 3);
                    }
                }

                // Check for a range first, then a number.
                if (CanLookAheadStartInclusive(2))
                {
                    if (PeekString(2).SequenceEqual(".."))
                    {
                        // A range.
                        // Advance by 2 since "..".
                        return MakeTokenAndTryAdvance(TokenType.DOT_DOT, 2);
                    }
                }

                return MakeTokenAndTryAdvance(TokenType.DOT, 1);
            }

            if (currentIsADot || IsNumeric(currChar))
            {
                // Several cases here.
                // Just '.' -> Token.Dot
                // 1.000 Decimal
                // 1.000f Float
                // 1_000_000 type numbers, type doesnt matter, simply a cosmetic look.

                startPos = _currentPosition;

                // Can be 0.5 or just .5 
                bool dotOnlyFraction = currentIsADot && IsNumeric(PeekNext());

                if (IsNumeric(Peek()) || dotOnlyFraction)
                {
                    bool decimalPointAlreadyDefined = false;
                    bool numberWithSeparators = false;

                    while (_currentPosition < _sourceLength)
                    {
                        char lastc = currChar;
                        currChar = _sourceCode[_currentPosition];

                        if (currChar == '.' && _sourceCode[_currentPosition + 1] == '.')
                        {
                            string str = _sourceCode[startPos.._currentPosition];
                            if (dotOnlyFraction) str = str.Insert(0, "0");
                            return MakeTokenAndTryAdvance(TokenType.NUMBER, 0, str, str);
                        }

                        if (currChar == '.')
                        {
                            if (decimalPointAlreadyDefined)
                            {
                                faultyCodeLine = GetCodeLineFromSource(_sourceCode, _currentLine).TrimStart();
                                ConstructAndThrowLexerException(_currentLine, _currentColumn, "Invalid number format: multiple decimal points found.", faultyCodeLine, PeekNextToken(), _fileName);
                            }
                            decimalPointAlreadyDefined = true;
                        }

                        if (currChar is 'E' or 'e')
                        {
                            // After seeing an 'E', the next character should be a digit or a sign.
                            char next = PeekNext();
                            if (!IsNumeric(next) && next != '+' && next != '-')
                            {
                                string faultyLine = GetCodeLineFromSource(_sourceCode, _currentLine);
                                ConstructAndThrowLexerException(_currentLine, _currentColumn + 1, "Scientific notation 'E' must be followed by digits.", faultyLine, PeekNextToken(), _fileName);
                            }
                            else
                            {
                                AdvancePosition();
                            }
                        }
                        else if (IsNumeric(currChar) ||
                            currChar == '.' ||
                            ((currChar == '-' || currChar == '+') && (lastc == 'E' || lastc == 'e')))
                        {
                            AdvancePosition();
                        }
                        else if (currChar == '_')
                        {
                            numberWithSeparators = true;
                            AdvancePosition();
                        }
                        else break;
                    }

                    // Check if the number ends with a dot.
                    if (_sourceCode[_currentPosition - 1] == '.')
                    {
                        faultyCodeLine = GetCodeLineFromSource(_sourceCode, _currentLine + 1).TrimStart();
                        ConstructAndThrowLexerException(_currentLine, _currentColumn, "Number literal cannot end with a decimal point.", faultyCodeLine, PeekNextToken(), _fileName);
                    }

                    // Float here.
                    if (Peek() == 'f')
                    {
                        char previousChar = _sourceCode[_currentPosition - 1];
                        if (char.IsDigit(previousChar))
                        {
                            AdvancePosition();
                        }
                    }

                    string lexeme = _sourceCode[startPos.._currentPosition];

                    if (numberWithSeparators)
                    {
                        lexeme = lexeme.Replace("_", string.Empty);
                    }

                    if (dotOnlyFraction) lexeme = lexeme.Insert(0, "0");

                    // We already advanced in the loop and "f" check.
                    return MakeTokenAndTryAdvance(TokenType.NUMBER, 0, lexeme);
                }

                // Just a dot.
                return MakeTokenAndTryAdvance(TokenType.DOT, 1);
            }

            // _ is used for pipes, next char must not be an identifier if it is for a pipe.
            if (currChar == '_' && !IsIdentifier(PeekNext())) return MakeTokenAndTryAdvance(TokenType.UNDERSCORE, 1);

            bool isFString = currChar == 'f' && PeekNext() == '"';

            // Lex an identifier, unless an F string.
            if (IsIdentifier(currChar) && !isFString)
            {
                startPos = _currentPosition;
                while (!_hasReachedEndInternal)
                {
                    if (IsIdentifier(Peek())) AdvancePosition();
                    else break;
                }

                ReadOnlySpan<char> identifierSpan = _sourceCode.AsSpan(startPos, _currentPosition - startPos);

                TokenType type = FluenceKeywords.GetKeywordType(identifierSpan);
                _currentColumnBeforeWhiteSpace = _currentColumn - identifierSpan.Length;

                if (type != TokenType.IDENTIFIER)
                {
                    return MakeTokenAndTryAdvance(type);
                }
                else
                {
                    if (identifierSpan.SequenceEqual("typeof"))
                    {
                        return MakeTokenAndTryAdvance(TokenType.TYPE_OF, 0, identifierSpan.ToString());
                    }
                    string text = StringPool.Intern(identifierSpan);
                    return MakeTokenAndTryAdvance(TokenType.IDENTIFIER, 0, text);
                }
            }

            if (CanLookAheadStartInclusive(2) && isFString)
            {
                AdvancePosition(2); // consume 'f' and '"'.
                return ScanString(true);
            }
            else if (currChar == '"')
            {
                AdvancePosition(); // consume '"'.
                return ScanString(false);
            }

            int errorColumn = _currentColumn;
            faultyCodeLine = GetCodeLineFromSource(_sourceCode, _currentLine);
            char invalidChar = _sourceCode[startPos];

            LexerExceptionContext context = new LexerExceptionContext()
            {
                FileName = _fileName,
                LineNum = _currentLine,
                Column = errorColumn,
                FaultyLine = faultyCodeLine,
                Token = new Token(TokenType.UNKNOWN, _sourceCode[_currentPosition].ToString())
            };
            throw new FluenceLexerException($"Invalid character '{invalidChar}' found in source. Could not generate appropriate Token.", context);
        }

        private static void ConstructAndThrowLexerException(int lineNum, int column, string errorText, string faultyLine, Token token, string fileName)
        {
            LexerExceptionContext context = new LexerExceptionContext()
            {
                FileName = fileName,
                LineNum = lineNum,
                Column = column,
                FaultyLine = faultyLine,
                Token = token
            };
            throw new FluenceLexerException(errorText, context);
        }

        private Token ScanString(bool isFString = false)
        {
            _sb.Clear();
            int stringOpenColumn = _currentColumn;
            int stringInitialLine = _currentLine;

            while (Peek() != '"' && !_hasReachedEndInternal)
            {
                char currentChar = AdvanceAndGet();

                if (currentChar == '\n')
                {
                    AdvanceCurrentLine();
                }

                if (currentChar == '\\')
                {
                    if (_hasReachedEndInternal) break;

                    char escapedChar = AdvanceAndGet();
                    switch (escapedChar)
                    {
                        case 'n': _sb.Append('\n'); break;
                        case 't': _sb.Append('\t'); break;
                        case 'r': _sb.Append('\r'); break;
                        case '"': _sb.Append('\"'); break;
                        case '\\': _sb.Append('\\'); break;
                        default:
                            _sb.Append('\\');
                            _sb.Append(escapedChar);
                            break;
                    }
                }
                else
                {
                    // It's a regular character.
                    _sb.Append(currentChar);
                }
            }

            if (_hasReachedEndInternal)
            {
                string initialLineContent = GetCodeLineFromSource(_sourceCode, stringInitialLine).TrimStart();
                string truncatedLine = FluenceDebug.TruncateLine(initialLineContent);

                ConstructAndThrowLexerException(stringInitialLine, stringOpenColumn, "Unclosed string literal. The file ended before a closing '\"' was found.", truncatedLine, PeekNextToken(), _fileName);
            }

            AdvancePosition();

            TokenType type = isFString ? TokenType.F_STRING : TokenType.STRING;

            return MakeTokenAndTryAdvance(type, 0, null!, _sb.ToString());
        }

        private char AdvanceAndGet()
        {
            char c = _sourceCode[_currentPosition];
            AdvancePosition();
            return c;
        }

        private Token ScanPipe()
        {
            // |>>=
            // |??, |>>, |~>,
            // ||, |>, |?
            // |

            int availableLength = _sourceLength - _currentPosition;
            ReadOnlySpan<char> peek = PeekString(Math.Min(4, availableLength));

            if (peek.Length >= 2)
            {
                switch (peek[1])
                {
                    case '>': // Could be |> or |>> or |>>=
                        if (peek.Length >= 4 && peek[2] == '>' && peek[3] == '=')
                        {
                            return MakeTokenAndTryAdvance(TokenType.REDUCER_PIPE, 4);
                        }
                        if (peek.Length >= 3 && peek[2] == '>')
                        {
                            return MakeTokenAndTryAdvance(TokenType.MAP_PIPE, 3);
                        }
                        return MakeTokenAndTryAdvance(TokenType.PIPE, 2);

                    case '?': // Could be |? or |??
                        if (peek.Length >= 3 && peek[2] == '?')
                        {
                            return MakeTokenAndTryAdvance(TokenType.GUARD_PIPE, 3);
                        }
                        return MakeTokenAndTryAdvance(TokenType.OPTIONAL_PIPE, 2);

                    case '|': // Must be ||
                        return MakeTokenAndTryAdvance(TokenType.OR, 2);
                }
            }

            return MakeTokenAndTryAdvance(TokenType.PIPE_CHAR, 1);
        }

        private Token ScanLessThanOperator()
        {
            // <||!=| <||==| <||??| <||<=| <||>=|
            // <||<|   <||>| 
            // <==|, <!=|, <<=|, <>=|, <??|, <n?| <~?|
            // <<|, <>|, <n|, <?| <<-
            // <=, <<, <|, <~|
            // <

            int availableLength = _sourceLength - _currentPosition;
            ReadOnlySpan<char> peek = PeekString(Math.Min(6, availableLength));

            // First we check 6 Char operators.
            if (peek.Length >= 6 && peek[1] == '|' && peek[2] == '|')
            {
                string slice6 = peek[..6].ToString();
                switch (slice6)
                {
                    case "<||!=|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_OR_NOT_EQUAL, 6);
                    case "<||==|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_OR_EQUAL, 6);
                    case "<||??|":
                        return MakeTokenAndTryAdvance(TokenType.OR_GUARD_CHAIN, 6);
                    case "<||<=|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_OR_LESS_EQUAL, 6);
                    case "<||>=|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_OR_GREATER_EQUAL, 6);
                }
            }

            if (peek.Length >= 5 && peek[1] == '|' && peek[2] == '|')
            {
                string slice5 = peek[..5].ToString();
                switch (slice5)
                {
                    case "<||<|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_OR_LESS, 5);
                    case "<||>|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_OR_GREATER, 5);
                }
            }

            // Now we check 4 Char operators.
            if (peek.Length >= 4 && peek[3] == '|')
            {
                string slice4 = peek[..4].ToString();
                switch (slice4)
                {
                    case "<==|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_EQUAL, 4);
                    case "<!=|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_NOT_EQUAL, 4);
                    case "<<=|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_LESS_EQUAL, 4);
                    case "<>=|":
                        return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_GREATER_EQUAL, 4);
                    case "<??|":
                        return MakeTokenAndTryAdvance(TokenType.GUARD_CHAIN, 4);
                    case "<~?|":
                        return MakeTokenAndTryAdvance(TokenType.OPTIONAL_SEQUENTIAL_REST_ASSIGN, 4);
                }
            }

            // <n?|, <n|, <n!|, <n!?|
            if (peek.Length >= 2 && char.IsDigit(_sourceCode[_currentPosition + 1]))
            {
                AdvancePosition();
                // Store the number for the Token in GetNextToken().
                string n = ReadNumber();

                if (Match("?|"))
                {
                    // We matched '<n?|'.
                    // Only assign the number as text/literal, the rest of the operator is in the TokenType.
                    return new Token(TokenType.OPTIONAL_ASSIGN_N, null!, n, (ushort)_currentLine, (ushort)_currentColumn);
                }

                if (Match("|"))
                {
                    return new Token(TokenType.CHAIN_ASSIGN_N, null!, n, (ushort)_currentLine, (ushort)_currentColumn);
                }

                // <n!|
                if (Match("!|"))
                {
                    return new Token(TokenType.CHAIN_N_UNIQUE_ASSIGN, null!, n, (ushort)_currentLine, (ushort)_currentColumn);
                }

                // <n!?|
                if (Match("!?|"))
                {
                    return new Token(TokenType.OPTIONAL_CHAIN_N_UNIQUE_ASSIGN, null!, n, (ushort)_currentLine, (ushort)_currentColumn);
                }

                string initialLineContent = GetCodeLineFromSource(_sourceCode, _currentLine).TrimStart();
                string truncatedLine = FluenceDebug.TruncateLine(initialLineContent);

                ConstructAndThrowLexerException(_currentLine, _currentColumn - 2, "Faulty chain assignment pipe operator detected, expected '<n|' or '<n?|' or '<|' or '<n!|' or '<n!?|' format.", truncatedLine, PeekNextToken(), _fileName);
            }

            if (peek.Length >= 3 && peek[2] == '|')
            {
                string slice3 = peek[..3].ToString();
                switch (slice3)
                {
                    case "<<|": return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_LESS, 3);
                    case "<>|": return MakeTokenAndTryAdvance(TokenType.COLLECTIVE_GREATER, 3);
                    case "<?|": return MakeTokenAndTryAdvance(TokenType.OPTIONAL_REST_ASSIGN, 3);
                    case "<~|": return MakeTokenAndTryAdvance(TokenType.SEQUENTIAL_REST_ASSIGN, 3);
                    case "<!|": return MakeTokenAndTryAdvance(TokenType.UNIQUE_REST_ASSIGN, 3);
                }
            }

            if (peek.Length >= 2 && (peek[1] == '|' || peek[1] == '<' || peek[1] == '='))
            {
                string slice2 = peek[..2].ToString();
                switch (slice2)
                {
                    case "<=": return MakeTokenAndTryAdvance(TokenType.LESS_EQUAL, 2);
                    case "<<": return MakeTokenAndTryAdvance(TokenType.BITWISE_LEFT_SHIFT, 2);
                    case "<|": return MakeTokenAndTryAdvance(TokenType.REST_ASSIGN, 2);
                }
            }

            return MakeTokenAndTryAdvance(TokenType.LESS, 1);
        }

        private void AdvanceCurrentLine()
        {
            _currentLine++;
            _currentColumn = 1;
        }

        private void AdvancePosition(int n = 1)
        {
            _currentColumn += n;
            _currentPosition += n;
        }

        private bool Match(string expected)
        {
            if (!CanLookAheadStartInclusive(expected.Length)) return false;
            if (!PeekString(expected.Length).SequenceEqual(expected)) return false;

            AdvancePosition(expected.Length);
            return true;
        }

        private char Peek() => _currentPosition >= _sourceLength ? '\0' : _sourceCode[_currentPosition];

        private char PeekNext() => _currentPosition + 1 >= _sourceLength ? '\0' : _sourceCode[_currentPosition + 1];

        private string ReadNumber()
        {
            int start = _currentPosition;
            while (char.IsDigit(Peek())) AdvancePosition();
            return _sourceCode[start.._currentPosition];
        }

        private ReadOnlySpan<char> PeekString(int length) => _sourceCode.AsSpan(_currentPosition, length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanLookAheadStartInclusive(int numberOfChars = 1) => _currentPosition + numberOfChars <= _sourceLength;

        private void SkipWhiteSpaceAndComments()
        {
            int commentStartLine = _currentLine;
            int commentStartCol = _currentColumn + 1;

            while (!_hasReachedEndInternal)
            {
                bool skippedSomethingThisPass = false;

                while (!_hasReachedEndInternal && IsWhiteSpace(_sourceCode[_currentPosition]))
                {
                    AdvancePosition();
                    skippedSomethingThisPass = true;
                }

                if (!_hasReachedEndInternal && _sourceCode[_currentPosition] == '#')
                {
                    // A conditional '#IF' block of code.
                    if (CanLookAheadStartInclusive(3) && PeekString(3).SequenceEqual("#IF"))
                    {
                        break;
                    }

                    // Check for multi-line comment: '#*'.
                    if (CanLookAheadStartInclusive(2) && _sourceCode[_currentPosition + 1] == '*')
                    {
                        int level = 0; // We can have #* inside #*, to not read first *# as end of multiline, we keep track of level.
                        commentStartCol += 2;
                        skippedSomethingThisPass = true;
                        AdvancePosition(2); // Consume '#*'.
                        bool didntEndMultiLineComment = true;

                        while (!_hasReachedEndInternal)
                        {
                            if (CanLookAheadStartInclusive(2) && _sourceCode[_currentPosition] == '#' && _sourceCode[_currentPosition + 1] == '*')
                            {
                                level++;
                            }
                            if (CanLookAheadStartInclusive(2) && _sourceCode[_currentPosition] == '*' && _sourceCode[_currentPosition + 1] == '#')
                            {
                                if (level > 0) level--;
                                else
                                {
                                    didntEndMultiLineComment = false;
                                    AdvancePosition(2); // Consume the '*#'.
                                    break;
                                }
                            }
                            if (Peek() == '\n') AdvanceCurrentLine();
                            AdvancePosition();
                        }

                        if (didntEndMultiLineComment)
                        {
                            string initialLineContent = GetCodeLineFromSource(_sourceCode, commentStartLine).TrimStart();
                            string truncatedLine = FluenceDebug.TruncateLine(initialLineContent);

                            ConstructAndThrowLexerException(commentStartLine, commentStartCol, "Unterminated multi-line comment. The file ended before a closing '*#' was found.", truncatedLine, PeekNextToken(), _fileName);
                        }
                    }
                    else // It's a single-line comment.
                    {
                        skippedSomethingThisPass = true;
                        while (!_hasReachedEndInternal && _sourceCode[_currentPosition] != '\n')
                        {
                            AdvancePosition();
                        }
                    }
                }

                if (!skippedSomethingThisPass)
                {
                    break;
                }
            }
        }

        private Token MakeTokenAndTryAdvance(TokenType type, int len = 0, string text = null!, object literal = null!)
        {
            AdvancePosition(len);
            return new Token(type, text, literal, (ushort)_currentLineBeforeWhiteSpace, (ushort)_currentColumnBeforeWhiteSpace);
        }

        internal static string GetCodeLineFromSource(string source, int lineNumber)
        {
            if (lineNumber <= 0)
                return string.Empty;

            ReadOnlySpan<char> span = source.AsSpan();
            int currentLine = 1;
            int lineStart = 0;

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] is '\r' or '\n')
                {
                    if (currentLine == lineNumber)
                        return span[lineStart..i].ToString();

                    // Handles \r\n as a single newline.
                    if (span[i] == '\r' && i + 1 < span.Length && span[i + 1] == '\n')
                        i++;

                    currentLine++;
                    lineStart = i + 1;
                }
            }

            return currentLine == lineNumber && lineStart < span.Length ? span[lineStart..].ToString() : string.Empty;
        }

        private static bool IsNumeric(char c) => c is >= '0' and <= '9';

        private static bool IsIdentifier(char c) => char.IsLetterOrDigit(c) || c is '\u009F' || c is '_';

        private static bool IsWhiteSpace(char c) => c is ' ' or '\t';
    }
}