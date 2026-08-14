using Fluence.Unity.Exceptions;
using Fluence.Unity.RuntimeTypes;
using Fluence.Unity.VirtualMachine;
using System.Text;
using static Fluence.Unity.FluenceByteCode;
using static Fluence.Unity.FluenceByteCode.InstructionLine;
using static Fluence.Unity.FluenceInterpreter;
using static Fluence.Unity.Token;

namespace Fluence.Unity
{
    internal sealed class FluenceParser
    {
        private FluenceLexer _lexer;

        /// <summary>
        /// A secondary, temporary lexer used exclusively during the pre-pass to tokenize
        /// the default value expressions for struct fields without disturbing the main lexer's state.
        /// </summary>
        private FluenceLexer _fieldLexer;

        private readonly ParseState _currentParseState;

        /// <summary>
        /// Marks the index of the last bytecode instruction, up to which the bytecode has already been passed through by
        /// the <see cref="FluenceOptimizer"/>.
        /// </summary>
        private int _lastOptimizationIndex;

        private readonly FluenceIntrinsics _intrinsicsManager;

        private readonly List<string> _allProjectFiles = new List<string>();

        private readonly List<List<Token>> _tokenStreams = new List<List<Token>>();

        private readonly ResettableObjectPool<FluenceLexer> _lexerPool = new ResettableObjectPool<FluenceLexer>(lexer => lexer.Reset(), 2);

        /// <summary>
        /// Indicates that we are parsing a multi-file Fluence project.
        /// </summary>
        private readonly bool _multiFileProject;

        /// <summary>
        /// The instance of <see cref="VirtualMachineConfiguration"/> dictating certain parsing and runtime rules.
        /// </summary>
        private readonly VirtualMachineConfiguration _vmConfiguration;

        /// <summary>
        /// Indicates which file, if in a multi-file Fluence project, is being parsed at the moment.
        /// </summary>
        private string _currentParsingFileName;

        /// <summary>
        /// Default line output for the parser's debug methods.
        /// </summary>
        private readonly TextOutputMethod _outputLine;

        private readonly Dictionary<int, int> _variableSlotMap = new Dictionary<int, int>();

        private readonly Dictionary<int, int> _tempSlotMap = new Dictionary<int, int>();

        private readonly int SelfHashCode = "self".GetHashCode();

        [Flags]
        internal enum OperandUsage
        {
            None = 0,
            Lhs = 1 << 0,
            Rhs = 1 << 1,
            Rhs2 = 1 << 2,
            Rhs3 = 1 << 3,

            LhsAndRhs = Lhs | Rhs,
            AllThree = Lhs | Rhs | Rhs2,
            AllFour = Lhs | Rhs | Rhs2 | Rhs3
        }

        private static readonly OperandUsage[] _operandUsageMap;

        static FluenceParser()
        {
            InstructionCode[] opcodes = (InstructionCode[])Enum.GetValues(typeof(InstructionCode));
            _operandUsageMap = new OperandUsage[opcodes.Max(op => (int)op) + 1];

            static void SetUsage(OperandUsage usage, params InstructionCode[] codes)
            {
                foreach (InstructionCode code in codes)
                {
                    _operandUsageMap[(int)code] = usage;
                }
            }

            // Three operands.
            SetUsage(OperandUsage.AllThree,
                InstructionCode.Add, InstructionCode.Subtract, InstructionCode.Multiply, InstructionCode.Divide,
                InstructionCode.Modulo, InstructionCode.Power, InstructionCode.Equal, InstructionCode.NotEqual,
                InstructionCode.LessThan, InstructionCode.GreaterThan, InstructionCode.LessEqual, InstructionCode.GreaterEqual,
                InstructionCode.And, InstructionCode.Or, InstructionCode.BitwiseAnd, InstructionCode.BitwiseOr,
                InstructionCode.BitwiseXor, InstructionCode.BitwiseLShift, InstructionCode.BitwiseRShift,
                InstructionCode.SetField, InstructionCode.GetElement, InstructionCode.SetElement,
                InstructionCode.CallFunction, InstructionCode.CallMethod, InstructionCode.CallStatic, InstructionCode.SetStatic,
                InstructionCode.AddAssign, InstructionCode.SubAssign, InstructionCode.MulAssign, InstructionCode.DivAssign,
                InstructionCode.ModAssign, InstructionCode.BranchIfEqual, InstructionCode.BranchIfNotEqual,
                InstructionCode.BranchIfGreaterThan, InstructionCode.BranchIfGreaterOrEqual, InstructionCode.BranchIfLessThan,
                InstructionCode.BranchIfLessOrEqual, InstructionCode.PushThreeParams, InstructionCode.IterNext, InstructionCode.IsType, InstructionCode.PushKeyValuePair,
                InstructionCode.Resume, InstructionCode.NewCoroutine);

            // Two operands.
            SetUsage(OperandUsage.LhsAndRhs,
                InstructionCode.Assign, InstructionCode.AssignIfNil, InstructionCode.GotoIfTrue, InstructionCode.GotoIfFalse,
                InstructionCode.Negate, InstructionCode.Not, InstructionCode.BitwiseNot,
                InstructionCode.NewInstance, InstructionCode.GetField, InstructionCode.NewRange,
                InstructionCode.PushElement, InstructionCode.GetLength, InstructionCode.GetStatic,
                InstructionCode.ToString, InstructionCode.NewLambda, InstructionCode.PushTwoParams,
                InstructionCode.NewIterator, InstructionCode.GetType, InstructionCode.Yield);

            // One operand.
            SetUsage(OperandUsage.Lhs,
                InstructionCode.Return, InstructionCode.PushParam, InstructionCode.LoadAddress,
                InstructionCode.Increment, InstructionCode.Decrement, InstructionCode.IncrementIntUnrestricted,
                InstructionCode.NewList, InstructionCode.Goto, InstructionCode.TryBlock, InstructionCode.Throw, InstructionCode.NewDictionary);

            // Zero operands.
            SetUsage(OperandUsage.None,
                InstructionCode.Terminate, InstructionCode.Skip, InstructionCode.CatchBlock,
                InstructionCode.SectionGlobal, InstructionCode.SectionLambdaEnd, InstructionCode.SectionLambdaStart, InstructionCode.Unknown);
        }

        /// <summary>
        /// A pool of lists for the initialization of arguments, be it expression, function call or other.
        /// </summary>
        readonly ResettableObjectPool<List<Value>> _lhsPool = new ResettableObjectPool<List<Value>>(list => list.Clear(), 2);

        /// <summary>
        /// Exposes the global scope of the current parsing state, primarily for the intrinsic registrar.
        /// </summary>
        internal FluenceScope CurrentParserStateGlobalScope => _currentParseState.GlobalScope;

        /// <summary>
        /// Exposes the final, compiled bytecode after the Parse() method has completed.
        /// </summary>
        public List<InstructionLine> CompiledCode => _currentParseState.CodeInstructions;

        internal FluenceIntrinsics Intrinsics => _intrinsicsManager;

        internal ParseState CurrentParseState => _currentParseState;

        internal FluenceLexer Lexer => _lexer;

        internal bool IsMultiFileProject => _multiFileProject;

        /// <summary>
        /// Manages the context for a `loop`, `while`, or `for` statement, tracking all `break` and `continue`
        /// instructions that need to be back-patched.
        /// </summary>
        internal sealed class LoopOrMatchContext
        {
            /// <summary>
            /// A list of `Goto` instructions generated by `continue` statements, which need their target addresses patched.
            /// Only used for loops.
            /// </summary>
            internal List<int> ContinuePatchAddresses { get; } = new List<int>();

            /// <summary>
            /// A list of `Goto` instructions generated by `break` statements, which need their target addresses patched.
            /// Used with both loops and match statements.
            /// </summary>
            internal List<int> BreakPatchAddresses { get; } = new List<int>();
        }

        /// <summary>
        /// Encapsulates all the mutable state required for a single parsing operation.
        /// </summary>
        internal sealed class ParseState
        {
            /// <summary>The main list of generated bytecode.</summary>
            internal List<InstructionLine> CodeInstructions = new List<InstructionLine>();

            /// <summary>A stack to manage nested loop contexts for `break` and `continue`.</summary>
            internal readonly Stack<LoopOrMatchContext> ActiveLoopContexts = new Stack<LoopOrMatchContext>();

            /// <summary>A temporary list that collects all function/method `Assign` instructions during parsing.</summary>
            internal readonly List<InstructionLine> FunctionVariableDeclarations = new List<InstructionLine>();

            /// <summary>If in a multi-line project, stores the file paths of the project's .fl Fluence scripts.</summary>
            internal readonly List<string> ProjectFilePaths = new List<string>();

            /// <summary> Holds the reference to the parser. </summary>
            internal readonly FluenceParser ParserInstance;

            /// <summary>
            /// A temporary list that collects bytecode instructions for the initialization of scope global variables.
            /// Those bytecodes are placed before call to main, but after functions have been assigned.
            /// </summary>
            internal readonly List<InstructionLine> ScriptInitializerCode = new List<InstructionLine>();

            /// <summary>
            /// A temporary list that collects bytecode instructions for the initialization of scope global variables.
            /// Those bytecodes are placed before call to main, after functions have been assigned.
            /// </summary>
            internal List<InstructionLine> LambdaBodyInstructions = new List<InstructionLine>();

            /// <summary>
            /// Indicates whether the current expression, statement is inside a function, or within a raw scope.
            /// </summary>
            internal bool IsParsingFunctionBody { get; set; }

            /// <summary>
            /// Indicates whether the current expression, statement is inside a lambda.
            /// </summary>
            internal bool IsParsingLambdaBody { get; set; }

            /// <summary>
            /// Indicates whether the current expression, statement is static solid variable of a struct, or just a global static solid variable.
            /// </summary>
            internal bool IsParsingStaticSolid { get; set; }

            /// <summary>The struct symbol currently being parsed, or null.</summary>
            internal StructSymbol CurrentStructContext { get; set; }

            /// <summary>The top-level global scope.</summary>
            internal FluenceScope GlobalScope { get; }

            /// <summary>The current scope (global or a namespace) the parser is in.</summary>
            internal FluenceScope CurrentScope { get; set; }

            /// <summary>A dictionary of all declared namespaces.</summary>
            internal Dictionary<int, FluenceScope> NameSpaces { get; } = new Dictionary<int, FluenceScope>();

#if DEBUG
            internal Dictionary<string, FluenceScope> NameSpacesDebug { get; } = new Dictionary<string, FluenceScope>();
#endif

            /// <summary>A counter for generating unique temporary variable names.</summary>
            internal int NextTempNumber;

            internal readonly Dictionary<int, VariableValue> LocalVariableInterner = new Dictionary<int, VariableValue>();

            internal void ResetLocalInterner() => LocalVariableInterner.Clear();

            internal void AddFunctionVariableDeclaration(InstructionLine instructionLine) => FunctionVariableDeclarations.Add(instructionLine);

            /// <summary>
            /// Indicates that we are working with test code, usually test units, which use incomplete code,
            /// simple statements or expressions. In this case we add all instructions into <see cref="CodeInstructions"/> regardless of state.
            /// </summary>
            internal bool AllowTestCode { get; set; }

            internal void AddCodeInstruction(InstructionLine instructionLine)
            {
                Token token = ParserInstance._lexer.PeekCurrentToken();

                instructionLine.SetDebugInfo(token.ColumnInSourceCode, token.LineInSourceCode, ParserInstance._multiFileProject ? ProjectFilePaths.IndexOf(ParserInstance._currentParsingFileName) : -1);

                if (IsParsingLambdaBody)
                {
                    LambdaBodyInstructions.Add(instructionLine);
                    return;
                }

                if (!AllowTestCode && (!IsParsingFunctionBody || IsParsingStaticSolid))
                {
                    if (instructionLine.Instruction == InstructionCode.Assign)
                    {
                        instructionLine.Instruction = InstructionCode.AssignIfNil;
                    }

                    ScriptInitializerCode.Add(instructionLine);
                    return;
                }

                CodeInstructions.Add(instructionLine);
            }

            public ParseState(FluenceParser parser)
            {
                ParserInstance = parser;
                IsParsingFunctionBody = false;
                GlobalScope = new FluenceScope(null!, "Global", false, true);
                CurrentScope = GlobalScope;
            }

            /// <summary>
            /// Inserts the collected function declarations into the main bytecode stream,
            /// typically right before the final `Call Main` instruction.
            /// </summary>
            internal void InsertFunctionVariableDeclarations()
            {
                CodeInstructions.InsertRange(CodeInstructions.Count, FunctionVariableDeclarations);
            }
        }

        internal FluenceParser(string root, VirtualMachineConfiguration config, TextOutputMethod outLine, TextOutputMethod outNormal, TextInputMethod input, TextOutputMethod outError)
        {
            _vmConfiguration = config;
            _currentParseState = new ParseState(this);
            _multiFileProject = true;

            StageCoreLibraries(root);
            _allProjectFiles.AddRange(Directory.GetFiles(root, "*.fl", SearchOption.AllDirectories));
            _currentParseState.ProjectFilePaths.AddRange(_allProjectFiles);

            _outputLine = outLine;
            _intrinsicsManager = new FluenceIntrinsics(this, outLine, input, outNormal, outError);
            _intrinsicsManager.RegisterCoreGlobals();
        }

        internal FluenceParser(FluenceLexer lexer, VirtualMachineConfiguration config, TextOutputMethod outLine, TextOutputMethod outNormal, TextInputMethod input, TextOutputMethod outError)
        {
            _vmConfiguration = config;
            _currentParseState = new ParseState(this);
            _lexer = lexer;

            _outputLine = outLine;
            _intrinsicsManager = new FluenceIntrinsics(this, outLine, input, outNormal, outError);
            _intrinsicsManager.RegisterCoreGlobals();
        }

        /// <summary>
        /// Parses the entire source code. If allowTestCode is true, parses partial code
        /// ( Only used in tests to test small snippets, this omits the Main function declaration
        /// and Main function call bytecode checks. )
        /// </summary>
        /// <param name="allowTestCode"></param>
        internal void Parse(bool allowTestCode = false)
        {
            _currentParseState.AllowTestCode = allowTestCode;

            if (_multiFileProject)
            {
                ParseProjectTokens();
            }
            else
            {
                ParseTokens();
            }

            if (_vmConfiguration.ExecutionEndPoint == VirtualMachineConfiguration.ExecutionPipelineEndpoint.StopAtLexer)
            {
                return;
            }

            _lhsPool.Clear();

            if (!allowTestCode)
            {
                FunctionSymbol mainFunctionSymbol = FindEntryPoint() ?? throw ConstructParserException("Could not find a 'Main' function entry point.", new Token(TokenType.UNKNOWN));
                _currentParseState.GlobalScope.Declare("Main".GetHashCode(), mainFunctionSymbol);
            }

            if (_vmConfiguration.EmitSectionGlobal)
            {
                // An indicator that we are entering the setup phase, and the global declarations.
                _currentParseState.CodeInstructions.Add(new InstructionLine(InstructionCode.SectionGlobal, null!));
            }

            // We first insert the function declarations.
            // Then we insert the all the scopes' global variables' intialization bytecodes.
            // Finally we call Main.
            _currentParseState.InsertFunctionVariableDeclarations();

            if (_currentParseState.ScriptInitializerCode.Count > 0)
            {
                _currentParseState.CodeInstructions.InsertRange(_currentParseState.CodeInstructions.Count, _currentParseState.ScriptInitializerCode);
            }

            _currentParseState.IsParsingFunctionBody = true;
            _currentParseState.AddCodeInstruction(
                new InstructionLine(
                    InstructionCode.CallFunction,
                    new TempValue(_currentParseState.NextTempNumber++),
                    GetOrCreateVariable("Main__0"),
                    NumberValue.Zero
                )
            );

            // We add a universal TERMINATE instruction for the VM, at the very end of the generated byte code.
            // Both for convenience and so that we dont end on dangling instructions.
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Terminate, null!));

            _lexer.ClearTokens();
        }

        /// <summary>
        /// Prepares a project directory for compilation by copying required core libraries.
        /// It reads an 'Imports.fldef' file and copies the specified .fl files from the
        /// application's 'Core' directory into the project directory.
        /// </summary>
        /// <param name="projectRoot">The root directory of the Fluence project to be compiled.</param>
        public void StageCoreLibraries(string projectRoot)
        {
            string importFilePath = Path.Combine(projectRoot, "Imports.fldef");

            if (!File.Exists(importFilePath))
            {
                return;
            }

            string coreLibraryDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core");
            if (!Directory.Exists(coreLibraryDir))
            {
                _outputLine("Warning: 'Core' library directory not found. Cannot stage intrinsic Fluence files.");
                return;
            }

            string[] libraryNames = File.ReadAllLines(importFilePath);

            foreach (string libName in libraryNames)
            {
                string trimmedName = libName.Trim();
                if (string.IsNullOrEmpty(trimmedName) || trimmedName.StartsWith('#'))
                {
                    continue;
                }

                string templatedName = $"{trimmedName}.fl";

                string sourcePath = Path.Combine(coreLibraryDir, templatedName);
                string destPath = Path.Combine(projectRoot, templatedName);

                if (File.Exists(sourcePath) && !File.Exists(destPath))
                {
                    try
                    {
                        File.Copy(sourcePath, destPath);
                    }
                    catch (Exception ex)
                    {
                        throw ConstructParserException($"Error staging library '{trimmedName}': {ex.Message}", NoUse);
                    }
                }
            }
        }

        internal void AddNameSpace(FluenceScope nameSpace)
        {
            _currentParseState.NameSpaces.TryAdd(nameSpace.Name.GetHashCode(), nameSpace);
        }

        // This doesn't quite suppport ExecutionPipelineEndPoint.
        private void ParseProjectTokens()
        {
            foreach (string path in _allProjectFiles)
            {
                _currentParsingFileName = path;
                FluenceLexer lexer = new FluenceLexer(File.ReadAllText(path), path);
                lexer.LexFullSource();

#if DEBUG
                lexer.DumpTokenStream($"Initial Token Stream (Before Pre-Parsing declarations) | {path}", _outputLine);
#endif

                _lexer = lexer;

                int parsedUpTo = 0;
                while (parsedUpTo < _lexer.TokenCount)
                {
                    int currentTarget = _lexer.TokenCount;
                    ParseDeclarations(parsedUpTo, currentTarget);
                    parsedUpTo = currentTarget;
                }

                _tokenStreams.Add(_lexer.AllTokens());
            }

            for (int i = 0; i < _allProjectFiles.Count; i++)
            {
                _currentParsingFileName = _allProjectFiles[i];
                _lexer = new FluenceLexer(_tokenStreams[i], _allProjectFiles[i]);

#if DEBUG
                _lexer.DumpTokenStream($"Token stream after parsing declarations. | {_currentParsingFileName}", _outputLine);
#endif

                while (!_lexer.HasReachedEnd)
                {
                    if (_lexer.TokenTypeMatches(TokenType.EOF))
                    {
                        _lexer.Advance();
                        break;
                    }

                    ParseStatement();
                }
            }
        }

        private void ParseTokens()
        {
            _lexer.LexFullSource();

            if (_vmConfiguration.ExecutionEndPoint == VirtualMachineConfiguration.ExecutionPipelineEndpoint.StopAtLexer)
            {
                return;
            }

#if DEBUG
            _lexer.DumpTokenStream("Initial Token Stream (Before Pre-Parsing declarations)", _outputLine);
#endif

            int parsedUpTo = 0;
            while (parsedUpTo < _lexer.TokenCount)
            {
                int currentTarget = _lexer.TokenCount;
                ParseDeclarations(parsedUpTo, currentTarget);
                parsedUpTo = currentTarget;
            }

#if DEBUG
            _lexer.DumpTokenStream("Token stream after parsing declarations.", _outputLine);
#endif

            while (!_lexer.HasReachedEnd)
            {
                // We reached end of file, so we just quit.
                if (_lexer.TokenTypeMatches(TokenType.EOF))
                {
                    _lexer.Advance();
                    break;
                }

                ParseStatement();
            }
        }

        /// <summary>
        /// Searches for the main entry point function "Main" for the program.
        /// </summary>
        /// <returns>The FunctionSymbol for the entry point, or null if not found.</returns>
        private FunctionSymbol FindEntryPoint()
        {
            int mainHash = "Main__0".GetHashCode();

            if (_currentParseState.GlobalScope.TryResolve(mainHash, out Symbol? globalMainSymbol))
            {
                return (FunctionSymbol)globalMainSymbol;
            }

            foreach (FluenceScope scope in _currentParseState.NameSpaces.Values)
            {
                if (scope.TryResolve(mainHash, out Symbol mainFunc))
                {
                    return (FunctionSymbol)mainFunc;
                }
            }

            return null!; // No entry point found.
        }

        /// <summary>
        /// The main entry point for the first-pass. It initiates a recursive scan of the token stream
        /// to build the entire symbol and namespace hierarchy.
        /// </summary>
        private void ParseDeclarations(int start, int end)
        {
            int currentIndex = start;
            while (currentIndex < end)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(currentIndex + 1);

                if (type == TokenType.EOF) break;

                if (type == TokenType.SPACE)
                {
                    int namespaceNameIndex = currentIndex + 1;
                    int namespaceEndIndex = FindMatchingBrace(namespaceNameIndex);
                    string namespaceName = _lexer.PeekAheadByN(namespaceNameIndex + 1).Text;

                    int hash = namespaceName.GetHashCode();

                    FluenceScope parentScope = _currentParseState.CurrentScope;
                    FluenceScope namespaceScope = _currentParseState.NameSpaces.TryGetValue(hash, out FluenceScope scope)
                        ? scope
                        : new FluenceScope(parentScope, namespaceName, false);

                    _currentParseState.NameSpaces.Add(hash, namespaceScope);

#if DEBUG
                    _currentParseState.NameSpacesDebug.Add(namespaceName, namespaceScope);
#endif

                    _currentParseState.CurrentScope = namespaceScope;
                    ParseDeclarations(namespaceNameIndex + 2, namespaceEndIndex + 1);

                    _currentParseState.CurrentScope = parentScope;
                    currentIndex = namespaceEndIndex + 1;
                    continue;
                }

                if (type == TokenType.ENUM)
                {
                    int declarationStartIndex = currentIndex;
                    int declarationEndIndex = FindMatchingBrace(declarationStartIndex + 1);

                    ParseEnumDeclaration(declarationStartIndex, declarationEndIndex);
                    _lexer.EraseTokenRange(declarationStartIndex, declarationEndIndex);
                    continue;
                }
                else if (type == TokenType.TRAIT)
                {
                    int declarationStartIndex = currentIndex;
                    int declarationEndIndex = FindMatchingBrace(declarationStartIndex + 1);

                    ParseTraitDeclaration(declarationStartIndex, declarationEndIndex);
                    _lexer.EraseTokenRange(declarationStartIndex, declarationEndIndex);
                    continue;
                }
                else if (type == TokenType.FUNC)
                {
                    int declarationStartIndex = currentIndex;
                    int declarationEndIndex = FindFunctionHeaderDeclarationEnd(declarationStartIndex);

                    ParseFunctionHeaderDeclaration(declarationStartIndex, declarationEndIndex);

                    int functionEndIndex = FindFunctionBodyEnd(declarationEndIndex);
                    currentIndex = functionEndIndex + 1;
                    continue;
                }
                else if (type == TokenType.STRUCT)
                {
                    int declarationStartIndex = currentIndex;
                    int declarationEndIndex = FindMatchingBrace(declarationStartIndex + 1);

                    ParseStructDeclaration(declarationStartIndex, declarationEndIndex);

                    currentIndex = declarationEndIndex;
                    continue;
                }
                else if (type == TokenType.CONDITIONAL_IF)
                {
                    int declarationStartIndex = currentIndex;
                    int declarationEndIndex = ParseConditionalBlockFirstPhase(declarationStartIndex);

                    currentIndex = declarationEndIndex;
                    continue;
                }

                currentIndex++;
            }
        }

        /// <summary>
        /// Finds the index of the matching closing brace '}' for an opening brace '{',
        /// starting its scan from a given index.
        /// </summary>
        /// <param name="startIndex">The index of a token *before* the block we want to find the end of.</param>
        /// <returns>The index of the matching closing '}' token.</returns>
        private int FindMatchingBrace(int startIndex)
        {
            int currentIndex = startIndex;

            while (_lexer.PeekTokenTypeAheadByN(currentIndex + 1) != TokenType.L_BRACE)
            {
                if (_lexer.PeekTokenTypeAheadByN(currentIndex + 1) == TokenType.EOF)
                {
                    Token errorToken = _lexer.PeekAheadByN(startIndex + 1);
                    throw ConstructParserException("Could not find an opening '{' to start the block scan of an Enum or Struct or Function body.", errorToken);
                }
                currentIndex++;
            }

            currentIndex++;
            int braceDepth = 1;

            while (braceDepth > 0)
            {
                if (currentIndex >= _lexer.TokenCount)
                {
                    Token eofToken = _lexer.PeekAheadByN(_lexer.TokenCount);
                    throw ConstructParserException("Unclosed block. Reached end of file while looking for matching '}' for Enum or Struct or Function body.", eofToken);
                }

                TokenType currentTokenType = _lexer.PeekTokenTypeAheadByN(currentIndex + 1);

                switch (currentTokenType)
                {
                    case TokenType.L_BRACE:
                        braceDepth++;
                        break;
                    case TokenType.R_BRACE:
                        braceDepth--;
                        break;
                }

                if (braceDepth == 0)
                {
                    return currentIndex;
                }

                currentIndex++;
            }

            return currentIndex;
        }

        /// <summary>
        /// Finds the index of the '=>' arrow token that signifies the end of a function's header.
        /// </summary>
        /// <param name="startIndex">The index of the token before the 'func' keyword.</param>
        /// <returns>The index of the '=>' token.</returns>
        private int FindFunctionHeaderDeclarationEnd(int startIndex)
        {
            int currentIndex = startIndex + 1;
            while (currentIndex < _lexer.TokenCount)
            {
                TokenType tokenType = _lexer.PeekTokenTypeAheadByN(currentIndex + 1);

                if (tokenType == TokenType.ARROW)
                {
                    return currentIndex;
                }

                if (tokenType == TokenType.EOF)
                {
                    Token errorToken = _lexer.PeekAheadByN(startIndex + 1);
                    throw ConstructParserException("Unterminated function header. Reached end of file before finding '=>'.", errorToken);
                }

                currentIndex++;
            }

            Token lastToken = _lexer.PeekAheadByN(_lexer.TokenCount);
            throw ConstructParserException("Unterminated function header. Could not find '=>'.", lastToken);
        }

        /// <summary>
        /// Finds the index of the last token in a function's body.
        /// </summary>
        /// <returns>The index of the last token in the function body.</returns>
        private int FindFunctionBodyEnd(int startIndex)
        {
            int currentIndex = startIndex + 1;
            TokenType bodyStartTokenType = _lexer.PeekTokenTypeAheadByN(currentIndex + 1);

            if (bodyStartTokenType == TokenType.L_BRACE)
            {
                return FindMatchingBrace(startIndex + 1);
            }

            int parenDepth = 0;
            while (true)
            {
                // Sanity check to prevent infinite loops on malformed code.
                if (currentIndex >= _lexer.TokenCount - 1)
                {
                    // Reached the end of the file, which can be a valid end for the last function.
                    return currentIndex;
                }

                TokenType currentTokenType = _lexer.PeekTokenTypeAheadByN(currentIndex + 1);

                if (currentTokenType == TokenType.L_PAREN) parenDepth++;
                else if (currentTokenType == TokenType.R_PAREN) parenDepth--;

                // A semicolon (EOL) only terminates the statement if we are not inside parentheses.
                if (currentTokenType == TokenType.EOL && parenDepth == 0)
                {
                    return currentIndex;
                }

                currentIndex++;
            }
        }

        /// <summary>
        /// Parses a function header during the first-pass to create its FunctionSymbol in the symbol table.
        /// This method only extracts the name and arity; it does not generate any bytecode.
        /// </summary>
        /// <param name="startTokenIndex">The index of the token before the 'func' keyword.</param>
        /// <param name="endTokenIndex">The index of the '=>' arrow token, marking the end of the header.</param>
        private void ParseFunctionHeaderDeclaration(int startTokenIndex, int endTokenIndex)
        {
            Token nameToken = _lexer.PeekAheadByN(startTokenIndex + 2);
            string funcName = nameToken.Text;

            int arity = 0;
            // Starts scanning for members after the opening '('.
            // aka `func Name (...`.
            int currentIndex = startTokenIndex + 3;

            List<string> paramaters = new List<string>();
            int refMask = 0;
            bool paramByRef = false;

            while (currentIndex < endTokenIndex)
            {
                TokenType currentTokenType = _lexer.PeekTokenTypeAheadByN(currentIndex + 1);

                if (currentTokenType == TokenType.REF)
                {
                    paramByRef = true;

                    if (arity >= 32) throw ConstructParserException("Argument limit (32) exceeded ( What are you even doing? ).", _lexer.PeekAheadByN(currentIndex + 1));

                    refMask |= 1 << arity;

                    if (_lexer.PeekTokenTypeAheadByN(currentIndex + 2) != TokenType.IDENTIFIER)
                    {
                        throw ConstructParserExceptionWithUnexpectedToken("Expected an argument identifier after a 'ref' keyword", _lexer.PeekCurrentToken());
                    }
                }
                if (currentTokenType == TokenType.IDENTIFIER)
                {
                    paramaters.Add(_lexer.PeekAheadByN(currentIndex + 1).Text);
                    if (paramByRef)
                    {
                        paramByRef = false;
                    }
                    arity++;
                    currentIndex++;
                }
                else if (currentTokenType is TokenType.COMMA or TokenType.R_PAREN)
                {
                    currentIndex++;
                }

                currentIndex++;
            }

            string parsedName = funcName.EndsWith($"__{arity}", StringComparison.Ordinal) ? funcName : Mangler.Mangle(funcName, arity);
            FunctionSymbol functionSymbol = new FunctionSymbol(parsedName, arity, -1, nameToken.LineInSourceCode, _currentParseState.CurrentScope, paramaters, refMask);

            _lexer.ModifyTokenAt(startTokenIndex + 1, new Token(TokenType.IDENTIFIER, parsedName, nameToken.Literal, nameToken.LineInSourceCode, nameToken.ColumnInSourceCode));
            _currentParseState.CurrentScope.Declare(parsedName.GetHashCode(), functionSymbol);
        }

        /// <summary>
        /// Scans a conditional compilation block (#IF) during the parser's first pass without consuming tokens.
        /// It evaluates the boolean expression and, if the condition is false, calculates the index of the
        /// token immediately following the block's closing brace, allowing the parser to skip it.
        /// If the condition is true, it returns the index of the opening brace to begin parsing.
        /// </summary>
        /// <param name="start">The starting index in the token stream of the '#IF' directive token.</param>
        /// <returns>
        /// If the condition is true, the index of the opening brace '{'.
        /// If the condition is false, the index of the token after the matching closing brace '}'.
        /// </returns>
        private int ParseConditionalBlockFirstPhase(int start)
        {
            // start + 1 is the CONDITIONAL_IF TOKEN.
            int currentIndex = start + 2;

            bool conditionMet = _vmConfiguration.CompilationSymbols.Contains(_lexer.PeekAheadByN(currentIndex).Text);
            currentIndex++;

            while (true)
            {
                TokenType opType = _lexer.PeekTokenTypeAheadByN(currentIndex);
                if (opType == TokenType.AND)
                {
                    currentIndex++; // Skip '&&'.
                    bool nextTerm = _vmConfiguration.CompilationSymbols.Contains(_lexer.PeekAheadByN(currentIndex).Text);
                    currentIndex++;
                    conditionMet = conditionMet && nextTerm;
                }
                else if (opType == TokenType.OR)
                {
                    currentIndex++; // Skip '||'.
                    bool nextTerm = _vmConfiguration.CompilationSymbols.Contains(_lexer.PeekAheadByN(currentIndex).Text);
                    currentIndex++;
                    conditionMet = conditionMet || nextTerm;
                }
                else
                {
                    break;
                }
            }

            if (!conditionMet)
            {
                int depth = 0;

                while (true)
                {
                    TokenType type = _lexer.PeekTokenTypeAheadByN(currentIndex);

                    if (type == TokenType.L_BRACE)
                    {
                        depth++;
                    }
                    else if (type == TokenType.R_BRACE)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return currentIndex;
                        }
                    }
                    currentIndex++;
                }
            }

            return currentIndex;
        }

        /// <summary>
        /// Parses a conditional compilation block (#IF) during the main bytecode generation pass.
        /// It evaluates the boolean expression following the '#IF' directive. If the condition is true,
        /// it proceeds to parse the enclosed block statement. If false, it consumes and discards
        /// all tokens until the matching closing brace is found.
        /// </summary>
        private void ParseConditionalBlock()
        {
            _lexer.Advance(); // Consume '#IF'.

            Token nameToken = _lexer.ConsumeToken();
            bool conditionMet = _vmConfiguration.CompilationSymbols.Contains(nameToken.Text);

            while (true)
            {
                TokenType opType = _lexer.PeekNextTokenType();
                if (opType == TokenType.AND)
                {
                    _lexer.Advance(); // Consume '&&'.
                    Token nextToken = _lexer.ConsumeToken();
                    conditionMet = conditionMet && _vmConfiguration.CompilationSymbols.Contains(nextToken.Text);
                }
                else if (opType == TokenType.OR)
                {
                    _lexer.Advance(); // Consume '||'.
                    Token nextToken = _lexer.ConsumeToken();
                    conditionMet = conditionMet || _vmConfiguration.CompilationSymbols.Contains(nextToken.Text);
                }
                else
                {
                    break;
                }
            }

            if (conditionMet)
            {
                ParseBlockStatement();
                return;
            }

            // Invalid target, we ignore the code block.
            int depth = 1;
            _lexer.Advance();

            while (true)
            {
                if (depth == 0)
                {
                    break;
                }

                TokenType type = _lexer.PeekNextTokenType();

                if (type == TokenType.L_BRACE)
                {
                    depth++;
                }
                else if (type == TokenType.R_BRACE)
                {
                    depth--;
                }

                _lexer.Advance();
            }
        }

        /// <summary>
        /// Parses a struct declaration during the first-pass to build its complete symbol.
        /// This involves identifying all fields, default value expressions, methods, and the constructor.
        /// </summary>
        /// <param name="startTokenIndex">The index of the token before the 'struct' keyword.</param>
        /// <param name="endTokenIndex">The index of the closing '}' token.</param>
        private void ParseStructDeclaration(int startTokenIndex, int endTokenIndex)
        {
            Token nameToken = _lexer.PeekAheadByN(startTokenIndex + 2);
            string structName = nameToken.Text;
            StructSymbol structSymbol = new StructSymbol(structName, _currentParseState.CurrentScope);

            int currentIndex = startTokenIndex + 3;
            bool solidField = false;
            bool argByRef = false;

            // We skip the traits of the struct for now, they must be parsed first in the first pass so we can find them.
            if (_lexer.PeekTokenTypeAheadByN(currentIndex) == TokenType.IMPL)
            {
                int implementationsStart = currentIndex + 1;

                while (_lexer.PeekTokenTypeAheadByN(implementationsStart) != TokenType.L_BRACE)
                {
                    if (_lexer.PeekTokenTypeAheadByN(implementationsStart) == TokenType.COMMA)
                    {
                        implementationsStart++;
                        continue;
                    }

                    implementationsStart++;
                }

                currentIndex = implementationsStart;
            }

            while (currentIndex < endTokenIndex)
            {
                Token token = _lexer.PeekAheadByN(currentIndex + 1);

                // Trailing semicolon in struct, ignorable.
                if (token.Type == TokenType.EOL)
                {
                    currentIndex++;
                    continue;
                }

                if (token.Type == TokenType.ENUM)
                {
                    int start = currentIndex + 1;
                    int end = start;

                    while (true)
                    {
                        end++;

                        if (_lexer.PeekTokenTypeAheadByN(end) == TokenType.R_BRACE)
                        {
                            break;
                        }
                    }

                    ParseDeclarations(start - 1, end - 1);
                    endTokenIndex -= end - start;
                    continue;
                }

                if (token.Type == TokenType.STRUCT)
                {
                    // The best way to handle nested structs is to extract the tokens of the nested struct, and whatever is inside it and put them
                    // at the end of the file just before the EOF token, this way the nesting is gone, the first phase still goes through them and if the nested struct also has nested 
                    // structs or enums, they'll get de-nested the same way, all until there are no nested structs in structs.
                    int nestedStart = currentIndex;
                    int scanIndex = nestedStart;
                    int depth = 0;
                    bool foundOpenBrace = false;

                    while (scanIndex < _lexer.TokenCount)
                    {
                        TokenType currentType = _lexer.PeekTokenTypeAheadByN(scanIndex);

                        if (currentType == TokenType.L_BRACE)
                        {
                            foundOpenBrace = true;
                            depth++;
                        }
                        else if (currentType == TokenType.R_BRACE)
                        {
                            depth--;
                        }
                        scanIndex++;

                        if (foundOpenBrace && depth == 0)
                        {
                            break;
                        }
                    }

                    int nestedEnd = scanIndex;

                    if (!foundOpenBrace || depth != 0)
                    {
                        throw ConstructParserException($"Unclosed nested struct inside '{structName}'.", token);
                    }

                    List<Token> nestedTokens = new List<Token>();
                    for (int i = nestedStart + 1; i < nestedEnd; i++)
                    {
                        nestedTokens.Add(_lexer.PeekAheadByN(i));
                    }

                    // Needs to be a separate loop, otherwise the parser will explode.
                    // Replacing the tokens with EOL ( which will be ignored just fine ) is much faster than removing a range of tokens
                    // from the list and shifting the huge token array.
                    for (int i = nestedStart; i < nestedEnd - 1; i++)
                    {
                        _lexer.ModifyTokenAt(i, Token.EOL);
                    }

                    _lexer.InsertBeforeEOF(nestedTokens);

                    // CurrentIndex will now encounter a bunch of EOLs, which it will skip.
                    continue;
                }

                if (token.Type == TokenType.SOLID)
                {
                    solidField = true;
                    currentIndex++;
                    continue;
                }

                if (token.Type == TokenType.REF)
                {
                    argByRef = true;

                    if (_lexer.PeekNextTokenType() != TokenType.IDENTIFIER)
                    {
                        throw ConstructParserExceptionWithUnexpectedToken("Expected an argument identifier after a 'ref' keyword", _lexer.PeekCurrentToken());
                    }
                    continue;
                }

                if (token.Type == TokenType.IDENTIFIER)
                {
                    structSymbol.Fields.Add(token.Text);

                    int statementEndIndex = currentIndex + 1;
                    while (statementEndIndex < endTokenIndex && _lexer.PeekTokenTypeAheadByN(statementEndIndex + 1) != TokenType.EOL)
                    {
                        statementEndIndex++;
                    }

                    List<Token> defaultValueTokens = new List<Token>();

                    for (int z = currentIndex + 3; z <= statementEndIndex; z++)
                    {
                        defaultValueTokens.Add(_lexer.PeekAheadByN(z));
                    }

                    if (solidField)
                    {
                        solidField = false;
                        // A workaround of sorts.
                        structSymbol.StaticFields.Add(token.Text, RuntimeValue.Nil);
                    }

                    structSymbol.DefaultFieldValuesAsTokens.TryAdd(token.Text, defaultValueTokens);

                    currentIndex = statementEndIndex + 1;
                    continue;
                }

                if (token.Type == TokenType.FUNC)
                {
                    Token funcToken = _lexer.PeekAheadByN(currentIndex + 2);
                    string funcName = funcToken.Text;

                    int headerEndIndex = currentIndex + 1;
                    while (headerEndIndex < endTokenIndex && _lexer.PeekTokenTypeAheadByN(headerEndIndex + 1) != TokenType.ARROW)
                    {
                        headerEndIndex++;
                    }

                    List<string> args = new List<string>();
                    int refMask = 0;
                    int arity = 0;

                    for (int argScanIndex = currentIndex + 3; argScanIndex < headerEndIndex; argScanIndex++)
                    {
                        if (_lexer.PeekTokenTypeAheadByN(argScanIndex + 1) == TokenType.REF)
                        {
                            argByRef = true;

                            if (arity >= 32) throw ConstructParserException("Argument limit (32) exceeded ( What are you even doing? ).", token);

                            refMask |= 1 << arity;

                            if (_lexer.PeekTokenTypeAheadByN(argScanIndex + 2) != TokenType.IDENTIFIER)
                            {
                                throw ConstructParserExceptionWithUnexpectedToken("Expected an argument identifier after a 'ref' keyword", _lexer.PeekCurrentToken());
                            }
                        }
                        else if (_lexer.PeekTokenTypeAheadByN(argScanIndex + 1) == TokenType.IDENTIFIER)
                        {
                            Token argToken = _lexer.PeekAheadByN(argScanIndex + 1);
                            args.Add(argToken.Text);
                            if (argByRef)
                            {
                                argByRef = false;
                            }
                            arity++;
                        }
                    }

                    FunctionValue functionValue = new FunctionValue(funcName, true, arity, -1, nameToken.LineInSourceCode, args, refMask, _currentParseState.CurrentScope);
                    string templated;

                    if (funcName == "init")
                    {
                        templated = Mangler.Mangle("init", args.Count);
                        if (!structSymbol.Constructors.TryAdd(templated, functionValue))
                        {
                            throw ConstructParserException($"Constructor with '{args.Count}' arity is already defined in the struct '{structName}'.", funcToken);
                        }

                        _lexer.ModifyTokenAt(currentIndex + 1, new Token(TokenType.IDENTIFIER, templated, nameToken.Literal, nameToken.LineInSourceCode, nameToken.ColumnInSourceCode));
                    }
                    else
                    {
                        templated = Mangler.Mangle(funcName, args.Count);
                        if (!structSymbol.Functions.TryAdd(templated, functionValue))
                        {
                            throw ConstructParserException($"Method '{funcName}' with '{args.Count}' arity is already defined in the struct '{structName}'.", funcToken);
                        }

                        _lexer.ModifyTokenAt(currentIndex + 1, new Token(TokenType.IDENTIFIER, templated, nameToken.Literal, nameToken.LineInSourceCode, nameToken.ColumnInSourceCode));
                    }

                    int functionBodyEndIndex = FindFunctionBodyEnd(headerEndIndex);
                    currentIndex = functionBodyEndIndex + 1;
                    continue;
                }

                currentIndex++;
            }

            if (!_currentParseState.CurrentScope.Declare(structName.GetHashCode(), structSymbol))
            {
                throw ConstructParserException($"A symbol named '{structName}' is already defined in this scope.", nameToken);
            }
        }

        private void ParseTraitDeclaration(int startTokenIndex, int endTokenIndex)
        {
            Token nameToken = _lexer.PeekAheadByN(startTokenIndex + 2);
            string traitName = nameToken.Text;
            TraitSymbol traitSymbol = new TraitSymbol(traitName);

            // Start scanning for members after the '{'.
            // `trait Name {`.
            int currentIndex = startTokenIndex + 3;

            bool solidField = false;
            while (currentIndex < endTokenIndex)
            {
                Token currentToken = _lexer.PeekAheadByN(currentIndex + 1);

                if (currentToken.Type == TokenType.SOLID)
                {
                    solidField = true;
                    currentIndex++;
                    continue;
                }

                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    string fieldName = currentToken.Text;
                    int fieldNameHash = fieldName.GetHashCode();

                    if (traitSymbol.FunctionSignatures.ContainsKey(fieldNameHash))
                    {
                        throw ConstructParserException($"Duplicate trait field '{fieldName}'.", currentToken);
                    }

                    traitSymbol.FieldSignatures.Add(fieldNameHash, fieldName);

                    int statementEndIndex = currentIndex;
                    List<Token> defaultValueTokens = new List<Token>();

                    if (_lexer.PeekTokenTypeAheadByN(statementEndIndex + 2) != TokenType.EOL)
                    {
                        while (statementEndIndex < endTokenIndex && _lexer.PeekTokenTypeAheadByN(statementEndIndex + 1) != TokenType.EOL)
                        {
                            statementEndIndex++;
                        }

                        for (int z = currentIndex + 3; z <= statementEndIndex; z++)
                        {
                            defaultValueTokens.Add(_lexer.PeekAheadByN(z));
                        }

                        if (solidField)
                        {
                            solidField = false;
                            // A workaround of sorts.
                            traitSymbol.StaticFields.Add(fieldName, RuntimeValue.Nil);
                        }
                    }

                    traitSymbol.DefaultFieldValuesAsTokens.TryAdd(fieldName, defaultValueTokens);

                    currentIndex = statementEndIndex == currentIndex ? statementEndIndex + 2 : statementEndIndex + 1;
                    continue;
                }
                else if (currentToken.Type == TokenType.FUNC)
                {
                    Token funcNameToken = _lexer.PeekAheadByN(currentIndex + 2);

                    if (funcNameToken.Type != TokenType.IDENTIFIER)
                    {
                        throw ConstructParserException("Expected a function name after keyword \"func\" in trait declaration", currentToken);
                    }

                    string funcName = funcNameToken.Text;
                    int arity = 0;

                    // Opening.
                    if (_lexer.PeekTokenTypeAheadByN(currentIndex + 3) != TokenType.L_PAREN)
                    {
                        throw ConstructParserException("Expected an opening parenthesis after function name in trait declaration", currentToken);
                    }

                    int currentLookAhead = currentIndex + 4;

                    while (_lexer.PeekTokenTypeAheadByN(currentLookAhead) == TokenType.IDENTIFIER)
                    {
                        arity++;
                        currentLookAhead++;
                    }

                    // Closing.
                    if (_lexer.PeekTokenTypeAheadByN(currentLookAhead) != TokenType.R_PAREN)
                    {
                        throw ConstructParserException("Expected closing parenthesis after function arguments in trait declaration", currentToken);
                    }

                    if (_lexer.PeekTokenTypeAheadByN(currentLookAhead + 1) != TokenType.EOL)
                    {
                        throw ConstructParserException("Expected a semicolon to mark the end of a function signature in trait declaration", currentToken);
                    }

                    string templatedName = Mangler.Mangle(funcName, arity);
                    int funcHash = templatedName.GetHashCode();

                    if (traitSymbol.FunctionSignatures.TryGetValue(funcHash, out TraitSymbol.FunctionSignature signature) && signature.Arity == arity)
                    {
                        throw ConstructParserException("Duplicate function signature in trait declaration", currentToken);
                    }

                    traitSymbol.FunctionSignatures.Add(funcHash, new TraitSymbol.FunctionSignature()
                    {
                        Arity = arity,
                        Hash = funcHash,
                        Name = templatedName,
                        IsAConstructor = templatedName.StartsWith("init__", StringComparison.Ordinal)
                    });

                    currentIndex = currentLookAhead + 1;
                    continue;
                }

                throw ConstructParserExceptionWithUnexpectedToken("Unknown token type in trait declaration", currentToken);
            }

            if (!_currentParseState.CurrentScope.Declare(traitName.GetHashCode(), traitSymbol))
            {
                throw ConstructParserException($"A trait symbol named '{traitName}' is already defined in this scope.", nameToken);
            }
        }

        /// <summary>
        /// Parses an enum declaration during the pre-pass to populate the symbol table.
        /// </summary>
        /// <param name="startTokenIndex">The index of the token before the 'enum' keyword.</param>
        /// <param name="endTokenIndex">The index of the closing '}' token.</param>
        private void ParseEnumDeclaration(int startTokenIndex, int endTokenIndex)
        {
            Token nameToken = _lexer.PeekAheadByN(startTokenIndex + 2);
            string enumName = nameToken.Text;
            EnumSymbol enumSymbol = new EnumSymbol(enumName);

            int currentValue = 0;
            // Start scanning for members after the '{'.
            // `enum Name {`.
            int currentIndex = startTokenIndex + 3;

            while (currentIndex < endTokenIndex)
            {
                Token currentToken = _lexer.PeekAheadByN(currentIndex + 1);

                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    string memberName = currentToken.Text;
                    if (enumSymbol.Members.ContainsKey(memberName))
                    {
                        throw ConstructParserException($"Duplicate enum member '{memberName}'.", currentToken);
                    }

                    EnumValue enumValue = new EnumValue(enumName, memberName, currentValue);
                    enumSymbol.Members.Add(memberName, enumValue);
                    currentValue++;
                }
                else if (currentToken.Type is not TokenType.COMMA)
                {
                    throw ConstructParserException($"Unexpected token '{currentToken.ToDisplayString()}' in enum body.", currentToken);
                }

                currentIndex++;
            }

            if (!_currentParseState.CurrentScope.Declare(enumName.GetHashCode(), enumSymbol))
            {
                throw ConstructParserException($"An enum symbol named '{enumName}' is already defined in this scope.", nameToken);
            }
        }

        /// <summary>
        /// Parses a single, complete statement. This method is the main dispatcher for the
        /// bytecode generation pass, determining whether to parse a declaration, a control flow
        /// statement, or a simple expression statement.
        /// </summary>
        private void ParseStatement()
        {
            if (_lexer.TokenTypeMatches(TokenType.EOL))
            {
                // It's a blank line. This is a valid, empty statement.
                _lexer.Advance();
                return;
            }

            switch (_lexer.PeekNextTokenType())
            {
                // Declarations & Scoping (Second Pass).
                case TokenType.FUNC: ParseFunction(); break;
                case TokenType.STRUCT: ParseStructStatement(); break;
                case TokenType.SPACE: ParseNameSpace(); break;
                case TokenType.USE: ParseUseStatement(); break;
                case TokenType.CONDITIONAL_IF: ParseConditionalBlock(); break;

                // Control Flow & Block Statements.
                case TokenType.IF: ParseIfStatement(); break;
                case TokenType.WHILE: ParseWhileStatement(false); break;
                case TokenType.UNTIL: ParseWhileStatement(true); break;
                case TokenType.FOR: ParseForStatement(); break;
                case TokenType.LOOP: ParseLoopStatement(); break;
                case TokenType.MATCH: ParseMatchStatement(); break;
                case TokenType.SOLID: ParseSolidStatement(); break;
                case TokenType.ROOT: ParseRootStatement(); break;
                case TokenType.UNLESS: ParseUnlessStatement(); break;
                case TokenType.TRY: ParseTryCatch(); break;

                // Simple Statements that must be terminated.
                case TokenType.RETURN:
                    ParseReturnStatement();
                    AdvanceAndExpect(TokenType.EOL, "Expected a ';' or newline after the return statement.");
                    break;
                case TokenType.BREAK:
                    _lexer.Advance(); // Consume 'break';

                    if (_currentParseState.ActiveLoopContexts.Count == 0)
                    {
                        throw ConstructParserExceptionWithUnexpectedToken("'break' cannot be used outside of a loop.", _lexer.PeekNextToken());
                    }

                    LoopOrMatchContext currentLoop = _currentParseState.ActiveLoopContexts.Peek();

                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
                    currentLoop.BreakPatchAddresses.Add(_currentParseState.CodeInstructions.Count - 1);

                    AdvanceAndExpect(TokenType.EOL, "Expected a ';' after the 'break' statement.");
                    break;
                case TokenType.CONTINUE:
                    _lexer.Advance(); // Consume 'continue'.

                    if (_currentParseState.ActiveLoopContexts.Count == 0)
                    {
                        throw ConstructParserExceptionWithUnexpectedToken("'continue' cannot be used outside of a loop.", _lexer.PeekNextToken());
                    }

                    LoopOrMatchContext currentLoop2 = _currentParseState.ActiveLoopContexts.Peek();

                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));

                    currentLoop2.ContinuePatchAddresses.Add(_currentParseState.CodeInstructions.Count - 1);

                    AdvanceAndExpect(TokenType.EOL, "Expected a ';' after the 'break' statement.");
                    break;
                default:
                    ParseAssignment();
                    AdvanceAndExpect(TokenType.EOL, "Expected a ';' to terminate the statement.");
                    break;
            }
        }

        /// <summary>
        /// Helper to parse a `space { ... }` block during the second pass.
        /// </summary>
        private void ParseNameSpace()
        {
            // In the second pass, we don't create a new namespace. We just enter it.
            AdvanceAndExpect(TokenType.SPACE, "Expected a 'space' keyword.");
            Token nameToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a namespace name.");
            AdvanceAndExpect(TokenType.L_BRACE, "Expected an opening '{' for the namespace body.");

            if (!_currentParseState.NameSpaces.TryGetValue(nameToken.Text.GetHashCode(), out FluenceScope? namespaceScope))
            {
                throw ConstructParserException($"Namespace '{nameToken.Text}' not found during second pass.", nameToken);
            }

            FluenceScope parentScope = _currentParseState.CurrentScope;
            _currentParseState.CurrentScope = namespaceScope!;

            // Parse all statements inside the block.
            while (!_lexer.TokenTypeMatches(TokenType.R_BRACE) && !_lexer.HasReachedEnd)
            {
                ParseStatement();
            }
            AdvanceAndExpect(TokenType.R_BRACE, "Expected a closing '}' for the namespace body.");

            _currentParseState.CurrentScope = parentScope;
        }

        private void ParseTryCatch()
        {
            _lexer.Advance(); // Consume 'try'.

            int jumpPatch = _currentParseState.CodeInstructions.Count;

            // try context.
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.TryBlock, null!));

            ParseStatementBody("A 'try' statement expects a '->' for a single line statement of code.");

            int tryBlockEnd = _currentParseState.CodeInstructions.Count;

            if (_lexer.PeekNextTokenType() != TokenType.CATCH)
            {
                throw ConstructParserException("A 'try' statement expects a 'catch' statement block.", _lexer.PeekCurrentToken());
            }

            _lexer.Advance(); // Consume 'catch'.

            VariableValue var = null!;

            if (_lexer.PeekNextTokenType() == TokenType.IDENTIFIER)
            {
                var = (VariableValue)ParseExpression();
            }

            // Catch does not require any Lhs, Rhs values, it simply indicates the beginning of a catch block.
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.CatchBlock, null!));

            ParseStatementBody("'catch' statement expects a '->' for a single line statement of code.");

            int catchBlockEnd = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[jumpPatch].Lhs = new TryCatchValue(tryBlockEnd, var?.Name!, -1, catchBlockEnd, var is not null);
        }

        /// <summary>
        /// Parses the body of a struct during the second (bytecode generation) pass.
        /// </summary>
        private void ParseStructStatement()
        {
            // This is basically a second pass of structs.
            // On the first pass we create the symbol table.
            // Fields, methods, init.
            // Now we only seek to generate bytecode of the functions and
            // Patch start addresses.

            _lexer.Advance(); // Consume 'struct'.
            Token nameToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a name for the struct.");

            string structName = nameToken.Text;
            HashSet<int> implementations = null;
            Dictionary<int, string> implementationNames = null;

            if (_lexer.PeekNextTokenType() == TokenType.IMPL)
            {
                _lexer.Advance();
                implementations = new HashSet<int>();
                implementationNames = new Dictionary<int, string>();

                while (_lexer.PeekNextTokenType() != TokenType.L_BRACE)
                {
                    if (_lexer.PeekNextTokenType() == TokenType.COMMA)
                    {
                        _lexer.Advance();
                        continue;
                    }

                    Token trait = _lexer.ConsumeToken();
                    int hash = trait.Text.GetHashCode();
                    implementationNames.Add(hash, trait.Text);
                    implementations.Add(hash);
                }
            }

            AdvanceAndExpect(TokenType.L_BRACE, $"Expected an opening '{{' for struct '{structName}'.");

            if (!_currentParseState.CurrentScope.TryResolve(structName.GetHashCode(), out Symbol symbol) || symbol is not StructSymbol structSymbol)
            {
                throw ConstructParserException($"Could not find the symbol for struct '{structName}'.", nameToken);
            }

            _currentParseState.CurrentStructContext = structSymbol;

            foreach (KeyValuePair<string, List<Token>> field in structSymbol.DefaultFieldValuesAsTokens)
            {
                Value defaultValueResult;
                string fieldName = field.Key;
                List<Token> expressionTokens = field.Value;

                if (structSymbol.StaticFields.ContainsKey(fieldName))
                {
                    if (expressionTokens.Count == 0)
                    {
                        throw ConstructParserException($"Expected an assignment of a value to a solid static struct field, value can not be Nil: {structSymbol}__Field:{fieldName}.", nameToken);
                    }

                    _fieldLexer = _lexer;
                    _lexer = _lexerPool.Get();
                    _lexer.Initialize(expressionTokens);

                    _currentParseState.IsParsingStaticSolid = true;
                    defaultValueResult = ResolveValue(ParseExpression());
                    _currentParseState.IsParsingStaticSolid = false;

                    _lexerPool.Return(_lexer);

                    _lexer = _fieldLexer; // Restore the main lexer.

                    // We call SetStatic for static fields, and add to the initializer code list.
                    // This way the values are assigned before the application runs.
                    _currentParseState.ScriptInitializerCode.Add(new InstructionLine(InstructionCode.SetStatic, structSymbol, new StringValue(fieldName), defaultValueResult));
                }
            }

            if (implementations != null)
            {
                foreach (int traitName in implementations)
                {
                    if (_currentParseState.CurrentScope.TryResolve(traitName, out Symbol symbol2) && symbol2 is TraitSymbol trait)
                    {
                        foreach (TraitSymbol.FunctionSignature requiredSignature in trait.FunctionSignatures.Values)
                        {
                            bool implementationFound = false;
                            bool nameFoundButArityMismatch = false;
                            int mismatchedArity = 0;
                            string fullNameFromTrait = requiredSignature.Name;

                            int delimiterIndex = fullNameFromTrait.LastIndexOf("__", StringComparison.Ordinal);

                            ReadOnlySpan<char> requiredNameSpan = (delimiterIndex == -1)
                                ? fullNameFromTrait.AsSpan()
                                : fullNameFromTrait.AsSpan(0, delimiterIndex);

                            Dictionary<string, FunctionValue>.ValueCollection structFunctionSignatures = requiredSignature.IsAConstructor ? structSymbol.Constructors.Values : structSymbol.Functions.Values;

                            foreach (FunctionValue implementedFunction in structFunctionSignatures)
                            {
                                if (requiredNameSpan.SequenceEqual(implementedFunction.Name))
                                {
                                    if (requiredSignature.Arity == implementedFunction.Arguments.Count)
                                    {
                                        implementationFound = true;
                                        break;
                                    }
                                    else
                                    {
                                        nameFoundButArityMismatch = true;
                                        mismatchedArity = implementedFunction.Arguments.Count;
                                    }
                                }
                            }

                            if (!implementationFound)
                            {
                                if (nameFoundButArityMismatch)
                                {
                                    throw ConstructParserException(
                                        $"Struct '{structSymbol.Name}' does not correctly implement trait '{trait.Name}'. " +
                                        $"The arity for function '{requiredNameSpan.ToString()}' is incorrect. " +
                                        $"Trait requires {requiredSignature.Arity} arguments, but an implementation was found with {mismatchedArity} arguments.",
                                        nameToken);
                                }
                                else
                                {
                                    throw ConstructParserException(
                                        $"Struct '{structSymbol.Name}' does not implement the required function " +
                                        $"'{requiredNameSpan.ToString()}({(requiredSignature.Arity != 0 ? $"arity__1" : "")})' from trait '{trait.Name}'.",
                                        nameToken);
                                }
                            }
                        }

                        foreach (KeyValuePair<int, string> item in trait.FieldSignatures)
                        {
                            if (!structSymbol.Fields.Contains(item.Value))
                            {
                                structSymbol.Fields.Add(item.Value);
                                structSymbol.DefaultFieldValuesAsTokens.Add(item.Value, trait.DefaultFieldValuesAsTokens[item.Value]);
                            }
                            else
                            {
                                throw ConstructParserException($"The struct symbol: {structSymbol.Name} already defines a field called: '{item.Value}' either from its base definition of from one of the implemented traits.", nameToken);
                            }
                        }

                        foreach (KeyValuePair<string, RuntimeValue> item in trait.StaticFields)
                        {
                            if (!structSymbol.StaticFields.ContainsKey(item.Key))
                            {
                                structSymbol.StaticFields.Add(item.Key, item.Value);
                            }
                            else
                            {
                                throw ConstructParserException($"The struct symbol: {structSymbol.Name} already defines a static solid field called: '{item.Value}' either from its base definition of from one of the implemented traits.", nameToken);
                            }
                        }

                        structSymbol.ImplementedTraits.Add(trait.Hash);
                    }
                    else
                    {
                        throw ConstructParserException($"The struct \"{structSymbol.Name}\" attempts to implement an unknown or invalid trait '{implementationNames![traitName]}'.", nameToken);
                    }
                }
            }

            // Empty struct.
            if (_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                _currentParseState.CurrentStructContext = null!;
                _lexer.Advance();
                return;
            }

            int currentIndex = 1;
            while (true)
            {
                TokenType currentTokenType = _lexer.PeekNextTokenType();

                if (currentTokenType == TokenType.FUNC)
                {
                    // It's a method or constructor, so we parse it fully.
                    // We can peek ahead to see if it's the special 'init' constructor.
                    bool isInit = _lexer.PeekAheadByN(2).Text.StartsWith("init__", StringComparison.Ordinal);
                    ParseFunction(true, isInit, structName);
                }
                else
                {
                    // It's not a function, so it must be a field declaration.
                    // In this second pass, we don't need to do anything with fields,
                    // so we just consume tokens until we find the end of the line.
                    _lexer.Advance();
                    currentIndex++;
                }

                // End of struct body.
                if (currentTokenType == TokenType.R_BRACE)
                {
                    break;
                }
            }

            _currentParseState.CurrentStructContext = null!;
        }

        /// <summary>
        /// Parses a `for` statement, dispatching to the correct helper based on whether
        /// it is a for-in loop or a C-style for loop.
        /// </summary>
        private void ParseForStatement()
        {
            _lexer.Advance(); // Consume 'for'.

            if (_lexer.PeekTokenTypeAheadByN(2) == TokenType.IN)
            {
                ParseForInStatement();
                return;
            }

            ParseForCStyleStatement();
        }

        /// <summary>
        /// Parses a C-style for loop.
        /// </summary>
        private void ParseForCStyleStatement()
        {
            ParseStatement();

            Value condition = ResolveValue(ParseExpression());
            int conditionCheckIndex = _currentParseState.CodeInstructions.Count;
            AdvanceAndExpect(TokenType.EOL, "Expected a ';' after the for-loop condition.");

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, condition));
            int loopExitPatchIndex = _currentParseState.CodeInstructions.Count - 1;

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int loopBodyJumpPatchIndex = _currentParseState.CodeInstructions.Count - 1;

            int incrementerStartIndex = _currentParseState.CodeInstructions.Count;
            ParseAssignment();
            AdvanceAndExpect(TokenType.EOL, "Expected a ';' after the 'for'-loop incrementer.");

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, new GoToValue(conditionCheckIndex - 1)));

            LoopOrMatchContext loopContext = new LoopOrMatchContext();
            _currentParseState.ActiveLoopContexts.Push(loopContext);
            int bodyStartIndex = _currentParseState.CodeInstructions.Count;

            ParseStatementBody("Expected an '->' for a single-line 'for' loop body.");

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, new GoToValue(incrementerStartIndex)));
            _currentParseState.CodeInstructions[loopBodyJumpPatchIndex].Lhs = new GoToValue(bodyStartIndex);

            int loopEndAddress = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[loopExitPatchIndex].Lhs = new GoToValue(loopEndAddress);

            PatchLoopExits(loopContext, loopEndAddress, incrementerStartIndex);

            _currentParseState.ActiveLoopContexts.Pop();
        }

        /// <summary>
        /// Parses a for-in loop.
        /// </summary>
        private void ParseForInStatement()
        {
            Token itemToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a loop variable name after 'for'.");
            VariableValue loopVariable = GetOrCreateVariable(itemToken.Text);

            AdvanceAndExpect(TokenType.IN, "Expected the 'in' keyword in a 'for-in' loop.");

            Value collectionExpr = ResolveValue(ParseExpression());

            LoopOrMatchContext loopContext = new LoopOrMatchContext();
            _currentParseState.ActiveLoopContexts.Push(loopContext);

            // Unless we use for in loop with a list, allocating a new list for a range, even if the range is 
            // only slightly large is very inefficient, so we create an iterator instead.
            if (collectionExpr is RangeValue range)
            {
                TempValue tempRangeReg = new TempValue(_currentParseState.NextTempNumber++);
                TempValue iteratorReg = new TempValue(_currentParseState.NextTempNumber++);
                TempValue valueReg = new TempValue(_currentParseState.NextTempNumber++);
                TempValue continueReg = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewRange, tempRangeReg, range));
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewIterator, iteratorReg, tempRangeReg));

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
                int jumpToConditionPatchIndex = _currentParseState.CodeInstructions.Count - 1;

                int loopBodyAddress = _currentParseState.CodeInstructions.Count;

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, loopVariable, valueReg));

                ParseStatementBody($"Expected an '->' for a single-line 'for-in' loop body.");

                int loopCheckAddress = _currentParseState.CodeInstructions.Count;
                _currentParseState.CodeInstructions[jumpToConditionPatchIndex].Lhs = new GoToValue(loopCheckAddress);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.IterNext, iteratorReg, valueReg, continueReg));

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, new GoToValue(loopBodyAddress), continueReg));

                int loopEndAddress = _currentParseState.CodeInstructions.Count;
                PatchLoopExits(loopContext, loopEndAddress, loopCheckAddress);
            }
            else
            {
                TempValue indexVar = new TempValue(_currentParseState.NextTempNumber++);
                TempValue collectionVar = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, collectionVar, collectionExpr));
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, indexVar, NumberValue.Zero));

                int loopTopAddress = _currentParseState.CodeInstructions.Count;

                TempValue lengthVar = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GetLength, lengthVar, collectionVar));

                TempValue conditionVar = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.LessThan, conditionVar, indexVar, lengthVar));

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, conditionVar));
                int loopExitPatchIndex = _currentParseState.CodeInstructions.Count - 1;

                TempValue currentElementVar = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GetElement, currentElementVar, collectionVar, indexVar));
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, loopVariable, currentElementVar));

                ParseStatementBody($"Expected an '->' for a single-line 'for-in' loop body.");

                int continueAddress = _currentParseState.CodeInstructions.Count;
                TempValue incrementedIndex = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Add, incrementedIndex, indexVar, NumberValue.One));
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, indexVar, incrementedIndex));

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, new GoToValue(loopTopAddress)));

                int loopEndAddress = _currentParseState.CodeInstructions.Count;
                _currentParseState.CodeInstructions[loopExitPatchIndex].Lhs = new GoToValue(loopEndAddress);
                PatchLoopExits(loopContext, loopEndAddress, continueAddress);
            }

            _currentParseState.ActiveLoopContexts.Pop();
        }

        /// <summary>
        /// Parses a while/until loop using Loop Inversion.
        /// Structure: Goto Check -> Label Body -> Body Code -> Label Check -> Condition Code -> BranchToBody.
        /// </summary>
        private void ParseWhileStatement(bool parseAsUntil)
        {
            _lexer.Advance(); // Consume 'while' or 'until'.

            int condStartIndex = _currentParseState.CodeInstructions.Count;

            Value condition = ResolveValue(ParseExpression());

            int condEndIndex = _currentParseState.CodeInstructions.Count;
            int condCount = condEndIndex - condStartIndex;

            List<InstructionLine> conditionInstructions = _currentParseState.CodeInstructions.GetRange(condStartIndex, condCount);

            _currentParseState.CodeInstructions.RemoveRange(condStartIndex, condCount);

            LoopOrMatchContext whileContext = new LoopOrMatchContext();
            _currentParseState.ActiveLoopContexts.Push(whileContext);

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int initialJumpIndex = _currentParseState.CodeInstructions.Count - 1;

            int loopBodyIndex = _currentParseState.CodeInstructions.Count;

            ParseStatementBody($"Expected an '->' for a single-line {(parseAsUntil ? "Until" : "While")} loop body.");

            int checkStartIndex = _currentParseState.CodeInstructions.Count;

            _currentParseState.CodeInstructions[initialJumpIndex].Lhs = new GoToValue(checkStartIndex);

            _currentParseState.CodeInstructions.AddRange(conditionInstructions);

            InstructionCode branchOp = parseAsUntil ? InstructionCode.GotoIfFalse : InstructionCode.GotoIfTrue;

            _currentParseState.AddCodeInstruction(new InstructionLine(branchOp, new GoToValue(loopBodyIndex), condition));

            int loopExitIndex = _currentParseState.CodeInstructions.Count;
            PatchLoopExits(whileContext, loopExitIndex, checkStartIndex);

            _currentParseState.ActiveLoopContexts.Pop();
        }

        /// <summary>
        /// Parses an infinite `loop { ... }` statement.
        /// Control can only exit this loop via a `break` statement.
        /// </summary>
        private void ParseLoopStatement()
        {
            _lexer.Advance(); // Consume 'loop'.

            int loopStartIndex = _currentParseState.CodeInstructions.Count;
            LoopOrMatchContext loopContext = new LoopOrMatchContext();
            _currentParseState.ActiveLoopContexts.Push(loopContext);

            ParseBlockStatement();

            // After the body executes, unconditionally jump back to the start.
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, new GoToValue(loopStartIndex)));

            int loopEndIndex = _currentParseState.CodeInstructions.Count;

            PatchLoopExits(loopContext, loopEndIndex, loopStartIndex);
            _currentParseState.ActiveLoopContexts.Pop();
        }

        /// <summary>
        /// Parses an Unless statement, the reverse of if, but without unless-else chains.
        /// </summary>
        private void ParseUnlessStatement()
        {
            _lexer.Advance(); // Consume 'unless'.

            Value condition = ResolveValue(ParseTernary());

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, null!, condition));

            int elsePatchIndex = _currentParseState.CodeInstructions.Count - 1;

            ParseStatementBody("Expected '->' token for a single-line Unless statement body.");

            int endAddress = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[elsePatchIndex].Lhs = new GoToValue(endAddress);
        }

        /// <summary>
        /// Parses an `if-else if-else` conditional statement chain.
        /// </summary>
        private void ParseIfStatement()
        {
            _lexer.Advance(); // Consume the 'if'.
            Value condition = ResolveValue(ParseTernary());

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, condition));

            int elsePatchIndex = _currentParseState.CodeInstructions.Count - 1;

            ParseStatementBody("Expected '->' token for a single-line if statement body.");

            // else, also handles else if, we just consume the else part, call parse if with the rest.
            if (_lexer.TokenTypeMatches(TokenType.ELSE))
            {
                int elseIfJumpOverIndex = _currentParseState.CodeInstructions.Count;
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));

                _lexer.Advance();

                int elseAddress = _currentParseState.CodeInstructions.Count;
                _currentParseState.CodeInstructions[elsePatchIndex].Lhs = new GoToValue(elseAddress);

                // This is an else-if, we just call ParseIf again.
                if (_lexer.TokenTypeMatches(TokenType.IF))
                {
                    ParseStatement();
                }
                else if (_lexer.TokenTypeMatches(TokenType.L_BRACE))
                {
                    ParseBlockStatement();
                }
                else // single line 'else'.
                {
                    AdvanceAndExpect(TokenType.THIN_ARROW, "Expected '->' token for single line if/else/else-if statement");
                    ParseStatement();
                }

                _currentParseState.CodeInstructions[elseIfJumpOverIndex].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
            }
            else
            {
                // No other else/else-ifs.
                int endAddress = _currentParseState.CodeInstructions.Count;
                _currentParseState.CodeInstructions[elsePatchIndex].Lhs = new GoToValue(endAddress);
            }
        }

        /// <summary>
        /// Parses a `return` statement. This can be a return with a value
        /// or a return without a value which implicitly returns nil.
        /// </summary>
        private void ParseReturnStatement()
        {
            _lexer.Advance(); // Consume 'return'.

            bool nilReturn = _lexer.TokenTypeMatches(TokenType.EOL);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Return, nilReturn ? NilValue.NilInstance : ResolveValue(ParseExpression())));
        }

        /// <summary>
        /// Helper to update the symbol table with the function's start address and
        /// add the final `Assign` instruction to the declaration list.
        /// </summary>
        private void UpdateFunctionSymbolsAndGenerateDeclaration(FunctionValue funcValue, Token nameToken, bool inStruct, bool isInit, string structName)
        {
            string functionName = nameToken.Text;
            int functionStartAddress = funcValue.StartAddress;

            if (inStruct)
            {
                bool isResolved = _currentParseState.CurrentScope.TryResolve(structName.GetHashCode(), out Symbol symbol);

                if (!isResolved || symbol is not StructSymbol)
                {
                    throw ConstructParserException($"Internal error: Could not resolve struct symbol '{structName}' in current scope.", nameToken);
                }

                StructSymbol structSymbol = (StructSymbol)symbol;

                if (!isInit)
                {
                    if (!structSymbol.Functions.TryGetValue(functionName, out FunctionValue functionValue))
                    {
                        throw ConstructParserException($"Internal error: Method '{funcValue.Name}' not found in the symbol table for struct '{structName}'.", nameToken);
                    }

                    functionValue!.SetStartAddress(functionStartAddress);
                    _currentParseState.AddFunctionVariableDeclaration(new InstructionLine(InstructionCode.Assign, GetOrCreateVariable($"{structName}.{functionValue.Name}"), functionValue));

                    return;
                }

                // Constructor init here.
                if (structSymbol.Constructors.Count == 0)
                {
                    throw ConstructParserException($"Internal error: No constructors found for struct '{structName}' in symbol table.", nameToken);
                }

                foreach (KeyValuePair<string, List<Token>> field in structSymbol.DefaultFieldValuesAsTokens)
                {
                    if (structSymbol.StaticFields.ContainsKey(field.Key))
                    {
                        continue;
                    }

                    Value defaultValueResult;
                    string fieldName = field.Key;
                    List<Token> expressionTokens = field.Value;

                    if (expressionTokens == null || expressionTokens.Count == 0)
                    {
                        _currentParseState.AddCodeInstruction(
                            new InstructionLine(
                                InstructionCode.SetField,
                                VariableValue.SelfVariable,
                                new StringValue(fieldName),
                                NilValue.NilInstance
                            )
                        );
                        continue;
                    }

                    _fieldLexer = _lexer;
                    _lexer = _lexerPool.Get();
                    _lexer.Initialize(expressionTokens);

                    defaultValueResult = ParseTernary();

                    _lexerPool.Return(_lexer);
                    _lexer = _fieldLexer; // Restore the main lexer.

                    _currentParseState.AddCodeInstruction(
                        new InstructionLine(
                            InstructionCode.SetField,
                            VariableValue.SelfVariable,
                            new StringValue(fieldName),
                            defaultValueResult // This will be the TempValue from the 'Add' instruction.
                        )
                    );
                }

                FunctionValue constructor = structSymbol.Constructors[nameToken.Text];
                constructor.SetStartAddress(functionStartAddress);
                _currentParseState.AddFunctionVariableDeclaration(new InstructionLine(InstructionCode.Assign, GetOrCreateVariable($"{structName}.{constructor.Name}"), constructor));
            }
            // Standalone function.
            else
            {
                // This will also work for functions defined in the current scope.
                if (!_currentParseState.CurrentScope.TryResolve(functionName.GetHashCode(), out Symbol symbol) || symbol is not FunctionSymbol functionSymbol)
                {
                    throw ConstructParserException($"Internal error: Could not resolve function symbol '{funcValue.Name}'. Function does not exist.", nameToken);
                }
                else
                {
                    functionSymbol.SetStartAddress(functionStartAddress);
                    _currentParseState.AddFunctionVariableDeclaration(new InstructionLine(InstructionCode.Assign, GetOrCreateVariable($"{funcValue.Name}"), funcValue));
                }
            }
        }

        /// <summary>
        /// Parses a complete function, method, or constructor declaration and generates its bytecode.
        /// This is the main dispatcher for all `func` keyword-related parsing.
        /// </summary>
        /// <param name="inStruct">True if parsing a method within a struct body.</param>
        /// <param name="isInit">True if the method being parsed is a constructor (`init`).</param>
        /// <param name="structName">The name of the struct, if `inStruct` is true.</param>
        private void ParseFunction(bool inStruct = false, bool isInit = false, string structName = null!)
        {
            _currentParseState.ResetLocalInterner();

            _currentParseState.IsParsingFunctionBody = true;
            (Token nameToken, List<string> parameters, int refMask) = ParseFunctionHeader();
            string functionName = nameToken.Text;

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int functionStartAddress = _currentParseState.CodeInstructions.Count;

            FunctionValue func = new FunctionValue(functionName, inStruct, parameters.Count, functionStartAddress, nameToken.LineInSourceCode, parameters, refMask, _currentParseState.CurrentScope);
            UpdateFunctionSymbolsAndGenerateDeclaration(func, nameToken, inStruct, isInit, structName);

            // Either => for one line, or => {...} for a block.
            if (_lexer.TokenTypeMatches(TokenType.L_BRACE))
            {
                ParseBlockStatement();
                if (_currentParseState.CodeInstructions[^1].Instruction != InstructionCode.Return)
                {
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Return, NilValue.NilInstance));
                }
            }
            else
            {
                Value returnValue = NilValue.NilInstance;
                // It is pleasant to do things like ... => self.x, self.y <~| ....
                // But those are statements not expressions, to allow them we check for, as of now, only assignment pipes
                // if found we parse a statement, and return nil.
                if (IsChainAssignmentAhead(out _))
                {
                    ParseStatement();
                }
                else
                {
                    returnValue = ResolveValue(ParseExpression());
                }
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Return, returnValue));
            }

            int afterBodyAddress = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[functionStartAddress - 1].Lhs = new GoToValue(afterBodyAddress + _currentParseState.LambdaBodyInstructions.Count);
            _currentParseState.IsParsingFunctionBody = false;

            int functionCodeEnd = afterBodyAddress;

            if (_vmConfiguration.OptimizeByteCode)
            {
                FluenceOptimizer.OptimizeChunk(
                    _currentParseState.CodeInstructions,
                    _currentParseState,
                    _lastOptimizationIndex,
                    _vmConfiguration
                );

                functionCodeEnd = _currentParseState.CodeInstructions.Count;
                _lastOptimizationIndex = functionCodeEnd;
            }

            int nextSlotIndex = 0;

            // Methods have an implicit 'self'. It always gets slot 0.
            if (inStruct)
            {
                int selfIndex = nextSlotIndex++;
                _variableSlotMap[SelfHashCode] = selfIndex;
            }

            // Parameters get the next available slots.
            for (int i = 0; i < func.Arguments.Count; i++)
            {
                _variableSlotMap[func.Arguments[i].GetHashCode()] = nextSlotIndex++;
            }

            for (int i = functionStartAddress; i < functionCodeEnd; i++)
            {
                InstructionLine insn = _currentParseState.CodeInstructions[i];
                if (insn.Instruction == InstructionCode.SectionLambdaStart)
                {
                    SkipNestedLambdaBlock(_currentParseState.CodeInstructions, ref i, functionCodeEnd);
                    continue;
                }

                OperandUsage usage = _operandUsageMap[(int)insn.Instruction];

                if (usage.HasFlag(OperandUsage.Lhs))
                    ProcessValue(insn.Lhs, _variableSlotMap, _tempSlotMap, ref nextSlotIndex);
                if (usage.HasFlag(OperandUsage.Rhs))
                    ProcessValue(insn.Rhs, _variableSlotMap, _tempSlotMap, ref nextSlotIndex);
                if (usage.HasFlag(OperandUsage.Rhs2))
                    ProcessValue(insn.Rhs2, _variableSlotMap, _tempSlotMap, ref nextSlotIndex);
                if (usage.HasFlag(OperandUsage.Rhs3))
                    ProcessValue(insn.Rhs3, _variableSlotMap, _tempSlotMap, ref nextSlotIndex);
            }

            func.TotalRegisterSlots = nextSlotIndex;

            if (_currentParseState.CurrentScope.TryResolve(func.Hash, out Symbol symbol) && symbol is FunctionSymbol funcSymbol)
            {
                funcSymbol.TotalRegisterSlots = func.TotalRegisterSlots;
                funcSymbol.BelongsToAStruct = inStruct;
            }

            for (int i = 0; i < _currentParseState.FunctionVariableDeclarations.Count; i++)
            {
                InstructionLine line = _currentParseState.FunctionVariableDeclarations[i];
                if (line.Rhs is FunctionValue fun && fun.Hash == func.Hash)
                {
                    _currentParseState.FunctionVariableDeclarations[i].Rhs = func;
                    break;
                }
            }

            if (inStruct)
            {
                // We need to update the stale FunctionValue in the StructSymbol's dictionary.
                // with the new, fully compiled one.
                if (isInit)
                {
                    _currentParseState.CurrentStructContext.Constructors[func.Name] = func;
                }
                else
                {
                    _currentParseState.CurrentStructContext.Functions[func.Name] = func;
                }
            }

            _tempSlotMap.Clear();
            _variableSlotMap.Clear();
            _currentParseState.ResetLocalInterner();
        }

        private VariableValue GetOrCreateVariable(string name, bool isReadonly = false, bool isGlobal = false)
        {
            int hash = name.GetHashCode();

            if (_currentParseState.LocalVariableInterner.TryGetValue(hash, out VariableValue variable))
            {
                return variable;
            }

            VariableValue newVal = new VariableValue(name, isReadonly);
            newVal.IsGlobal = isGlobal;
            _currentParseState.LocalVariableInterner[hash] = newVal;
            return newVal;
        }

        private static void ProcessValue(Value val, Dictionary<int, int> variableSlotMap, Dictionary<int, int> tempSlotMap, ref int nextSlotIndex)
        {
            if (val is VariableValue varVal)
            {
                if (varVal.IsGlobal) return;

                if (variableSlotMap.TryGetValue(varVal.Hash, out int index))
                {
                    varVal.RegisterIndex = index;
                }
                else
                {
                    int newIndex = nextSlotIndex++;
                    variableSlotMap[varVal.Hash] = newIndex;
                    varVal.RegisterIndex = newIndex;
                }
            }
            else if (val is TempValue temp)
            {
                if (tempSlotMap.TryGetValue(temp.Hash, out int index))
                {
                    temp.RegisterIndex = index;
                }
                else
                {
                    int newIndex = nextSlotIndex++;
                    tempSlotMap[temp.Hash] = newIndex;
                    temp.RegisterIndex = newIndex;
                }
            }
            else if (val is ReferenceValue reference)
            {
                ProcessValue(reference.Reference, variableSlotMap, tempSlotMap, ref nextSlotIndex);
            }
            else if (val is RangeValue range)
            {
                ProcessValue(range.Start, variableSlotMap, tempSlotMap, ref nextSlotIndex);
                ProcessValue(range.End, variableSlotMap, tempSlotMap, ref nextSlotIndex);
            }
            else if (val is TryCatchValue tryCatch && !string.IsNullOrEmpty(tryCatch.ExceptionVarName))
            {
                int hash = tryCatch.ExceptionVarName.GetHashCode();

                if (variableSlotMap.TryGetValue(hash, out int index))
                {
                    tryCatch.ExceptionAsVarRegisterIndex = index;
                }
                else
                {
                    int newIndex = nextSlotIndex++;
                    variableSlotMap[hash] = newIndex;
                    tryCatch.ExceptionAsVarRegisterIndex = newIndex;
                }
            }
        }

        /// <summary>
        /// Helper to parse the function header, from `func` up to the `=>`.
        /// </summary>
        /// <returns>A tuple containing the function's name token and a list of its parameter names.</returns>
        private (Token nameToken, List<string> parameters, int refMask) ParseFunctionHeader()
        {
            AdvanceAndExpect(TokenType.FUNC, "Expected the 'func' keyword.");
            Token nameToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a function name after 'func'.");
            AdvanceAndExpect(TokenType.L_PAREN, $"Expected an opening '(' for function '{nameToken.Text}' parameters.");

            int refMask = 0;
            List<string> parameters = new List<string>();
            int argIndex = 0;

            if (!_lexer.TokenTypeMatches(TokenType.R_PAREN))
            {
                do
                {
                    if (argIndex >= 32)
                    {
                        throw ConstructParserException("Argument limit (32) exceeded ( What are you even doing? ).", _lexer.PeekCurrentToken());
                    }

                    Token next = _lexer.ConsumeToken();

                    if (next.Type == TokenType.REF)
                    {
                        if (_lexer.PeekNextTokenType() != TokenType.IDENTIFIER)
                        {
                            throw ConstructParserExceptionWithUnexpectedToken("Expected an argument identifier after a 'ref' keyword", _lexer.PeekCurrentToken());
                        }

                        refMask |= 1 << argIndex;

                        Token paramToken = _lexer.ConsumeToken();
                        parameters.Add(paramToken.Text);
                    }
                    else
                    {
                        parameters.Add(next.Text);
                    }

                    argIndex++;

                } while (AdvanceTokenIfMatch(TokenType.COMMA));
            }

            AdvanceAndExpect(TokenType.R_PAREN, $"Expected a closing ')' after parameters for function '{nameToken.Text}'.");
            AdvanceAndExpect(TokenType.ARROW, $"Expected an '=>' to define the body of function '{nameToken.Text}'.");

            return (nameToken, parameters, refMask);
        }

        /// <summary>
        /// Parses a `match` statement or expression. This method acts as a dispatcher,
        /// determining whether to parse a statement-style (`case:`) or expression-style (`case ->`) match.
        /// </summary>
        /// <returns>
        /// A <see cref="Value"/> representing the result of the match if it's an expression,
        /// or a <see cref="NilValue"/> if it's a statement.
        /// </returns>
        private Value ParseMatchStatement()
        {
            if (_lexer.TokenTypeMatches(TokenType.MATCH))
            {
                // If we have lhs = match x
                // Then match falls to ParsePrimary(), which consumes it.
                // If it is just match x {...}, match token remains, so we consume it.
                _lexer.Advance();
            }

            Value matchOn = ResolveValue(ParseTernary());

            AdvanceAndExpect(TokenType.L_BRACE, "Expected an opening '{' to begin the match block.");

            // Check for match x { } empty match.
            if (_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                _lexer.Advance(); // Consume '}'.
                return NilValue.NilInstance; // An empty match does nothing and returns nil.
            }

            if (IsSwitchStyleMatch())
            {
                ParseMatchSwitchStyle(matchOn);
                return NilValue.NilInstance;
            }

            return ParseMatchExpressionStyle(matchOn);
        }

        /// <summary>
        /// Parses a switch-style `match` statement, which does not return a value and uses `break` and fallthrough.
        /// </summary>
        /// <param name="matchOn">The value being matched against, already parsed.</param>
        private void ParseMatchSwitchStyle(Value matchOn)
        {
            // matchOn is already resolved.
            LoopOrMatchContext context = new LoopOrMatchContext();
            _currentParseState.ActiveLoopContexts.Push(context);

            List<int> nextCasePatches = new List<int>();
            bool fallThrough = false;
            int fallThroughSkipIndex = -1;

            while (!_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                TokenType nextType = _lexer.PeekNextTokenType();

                if (nextType == TokenType.EOL)
                {
                    _lexer.Advance();
                    continue;
                }

                int nextCaseAddress = _currentParseState.CodeInstructions.Count;
                // Patch all fall-throughs from the previous case to jump here.
                foreach (int patch in nextCasePatches)
                {
                    _currentParseState.CodeInstructions[patch].Lhs = new GoToValue(nextCaseAddress);
                }
                nextCasePatches.Clear();

                if (nextType == TokenType.REST)
                {
                    _lexer.Advance();
                }
                else
                {
                    Value pattern = ResolveValue(ParseExpression());

                    TempValue condition = new TempValue(_currentParseState.NextTempNumber++);
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, condition, matchOn, pattern));
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, condition));

                    if (fallThrough)
                    {
                        _currentParseState.CodeInstructions[fallThroughSkipIndex].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
                        fallThrough = false;
                        fallThroughSkipIndex = 0;
                    }

                    nextCasePatches.Add(_currentParseState.CodeInstructions.Count - 1);
                }

                AdvanceAndExpect(TokenType.COLON, "Expected a ':' after the match case pattern.");

                // Parse the body after the colon.
                while (!_lexer.TokenTypeMatches(TokenType.R_BRACKET) && !_lexer.TokenTypeMatches(TokenType.REST))
                {
                    TokenType nextToken = _lexer.PeekNextTokenType();

                    if (nextToken == TokenType.BREAK)
                    {
                        _lexer.Advance();
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
                        context.BreakPatchAddresses.Add(_currentParseState.CodeInstructions.Count - 1);
                        break;
                    }

                    int lookahead = 1;
                    bool isNextCase = false;
                    while (true)
                    {
                        TokenType peekType = _lexer.PeekTokenTypeAheadByN(lookahead);
                        if (peekType == TokenType.COLON)
                        {
                            isNextCase = true;
                            break;
                        }
                        if (peekType is TokenType.R_BRACE or TokenType.EOF or TokenType.EOL)
                        {
                            // We hit the end of the block or a line break without finding a colon.
                            // This is not the start of a new case.
                            break;
                        }
                        lookahead++;
                    }

                    if (isNextCase)
                    {
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
                        fallThroughSkipIndex = _currentParseState.CodeInstructions.Count - 1;
                        fallThrough = true;
                        break;
                    }

                    // If none of the above, it's a regular statement in the case body.
                    ParseStatement();
                }

                nextCasePatches.Add(_currentParseState.CodeInstructions.Count - 1);
            }

            AdvanceAndExpect(TokenType.R_BRACE, "Expected a closing '}' to end the match statement.");

            _currentParseState.ActiveLoopContexts.Pop();

            int matchEndAddress = _currentParseState.CodeInstructions.Count;

            foreach (int patch in nextCasePatches)
            {
                _currentParseState.CodeInstructions[patch].Lhs = new GoToValue(matchEndAddress);
            }

            foreach (int patch in context.BreakPatchAddresses)
            {
                _currentParseState.CodeInstructions[patch].Lhs = new GoToValue(matchEndAddress);
            }
        }

        /// <summary>
        /// Parses a `match` expression that returns a value.
        /// </summary>
        /// <param name="matchOn">The value being matched against, already parsed.</param>
        /// <returns>A TempValue that will hold the result of the matched case at runtime.</returns>
        private TempValue ParseMatchExpressionStyle(Value matchOn)
        {
            // matchOn is already resolved.
            Value resolvedMatchOn = matchOn;
            TempValue result = new TempValue(_currentParseState.NextTempNumber++);

            List<int> endJumpPatches = new List<int>();
            bool hasRestCase = false;

            while (!_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                if (hasRestCase)
                {
                    throw ConstructParserException("The 'rest' case must be the final case in a match expression.", _lexer.PeekNextToken());
                }

                int nextCasePatchIndex;
                if (_lexer.TokenTypeMatches(TokenType.REST))
                {
                    hasRestCase = true;
                    _lexer.Advance(); // Consume the 'rest'.
                    nextCasePatchIndex = -1;
                }
                else
                {
                    Value pattern = ResolveValue(ParseTernary());

                    // This means that we are matching on some expression.
                    if (pattern is TempValue temp)
                    {
                        // We'll patch later.
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, temp));
                        nextCasePatchIndex = _currentParseState.CodeInstructions.Count - 1;
                    }
                    else
                    {
                        TempValue condition = new TempValue(_currentParseState.NextTempNumber++);
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, condition, resolvedMatchOn, pattern));

                        // We'll patch later.
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, condition));
                        nextCasePatchIndex = _currentParseState.CodeInstructions.Count - 1;
                    }
                }

                if (_lexer.TokenTypeMatches(TokenType.THIN_ARROW))
                {
                    AdvanceAndExpect(TokenType.THIN_ARROW, "Expected a '->' for the match case expression.");

                    Value caseResult = ResolveValue(ParseTernary());
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, result, caseResult));
                }
                else
                {
                    AdvanceAndExpect(TokenType.ARROW, "Expected a '=>' for the match case block.");
                    int instructionCountBeforeBlock = _currentParseState.CodeInstructions.Count;
                    ParseBlockStatement();

                    if (_currentParseState.CodeInstructions.Count == instructionCountBeforeBlock ||
                        _currentParseState.CodeInstructions[^1].Instruction != InstructionCode.Return)
                    {
                        throw ConstructParserException("A block body '=> { ... }' in a match expression must end with a 'return' statement.", _lexer.PeekNextToken());
                    }

                    Value returnedValue = _currentParseState.CodeInstructions[^1].Lhs;
                    _currentParseState.CodeInstructions[^1] = new InstructionLine(InstructionCode.Assign, result, returnedValue);
                }

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
                endJumpPatches.Add(_currentParseState.CodeInstructions.Count - 1);

                if (nextCasePatchIndex != -1)
                {
                    int nextCaseAddress = _currentParseState.CodeInstructions.Count;
                    _currentParseState.CodeInstructions[nextCasePatchIndex].Lhs = new GoToValue(nextCaseAddress);
                }

                AdvanceAndExpect(TokenType.EOL, "Expected a ';' after each match case.");
            }

            AdvanceAndExpect(TokenType.R_BRACE, "Expected a closing '}' to end the match expression.");

            if (!hasRestCase)
            {
                throw ConstructParserException("A 'match' expression that returns a value must be exhaustive and include a 'rest' case.", _lexer.PeekNextToken());
            }

            int matchEndAddress = _currentParseState.CodeInstructions.Count;

            foreach (int endJump in endJumpPatches)
            {
                _currentParseState.CodeInstructions[endJump].Lhs = new GoToValue(matchEndAddress);
            }

            return result;
        }

        /// <summary>
        /// A simple helper to generate an `Add` instruction to concatenate two string values.
        /// </summary>
        private Value ConcatenateStringValues(Value left, Value right)
        {
            if (left == null) return right;
            if (right == null) return left;

            TempValue temp = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Add, temp, left, right));
            return temp;
        }

        /// <summary>
        /// Parses a formatted string (f-string) literal, breaking it into literal text and interpolated expressions.
        /// It generates bytecode to evaluate expressions, convert them to strings, and concatenate all parts.
        /// </summary>
        /// <param name="literal">The raw string content from the token, without the leading 'f' or quotes.</param>
        /// <returns>A Value (StringValue or TempValue) that will hold the final concatenated string at runtime.</returns>
        private Value ParseFString(object literal)
        {
            string content = literal.ToString()!;
            List<Value> parts = new List<Value>();
            StringBuilder currentLiteral = new StringBuilder();

            int i = 0;
            while (i < content.Length)
            {
                char c = content[i];

                if (c == '{')
                {
                    if (i + 1 < content.Length && content[i + 1] == '{')
                    {
                        currentLiteral.Append('{');
                        i += 2;
                    }
                    else
                    {
                        if (currentLiteral.Length > 0)
                        {
                            parts.Add(new StringValue(currentLiteral.ToString()));
                            currentLiteral.Clear();
                        }

                        int exprStart = i + 1;
                        int braceDepth = 1;
                        int scan = exprStart;

                        while (scan < content.Length && braceDepth > 0)
                        {
                            if (content[scan] == '{') braceDepth++;
                            else if (content[scan] == '}') braceDepth--;
                            scan++;
                        }

                        if (braceDepth > 0)
                        {
                            throw ConstructParserException("Unclosed expression in f-string.", _lexer.PeekCurrentToken());
                        }

                        int exprLength = scan - 1 - exprStart;
                        string expressionSource = content.Substring(exprStart, exprLength);

                        Value exprValue = ParseSubExpression(expressionSource);

                        TempValue stringResult = new TempValue(_currentParseState.NextTempNumber++);
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.ToString, stringResult, exprValue));
                        parts.Add(stringResult);

                        i = scan;
                    }
                }
                else if (c == '}')
                {
                    if (i + 1 < content.Length && content[i + 1] == '}')
                    {
                        currentLiteral.Append('}');
                        i += 2;
                    }
                    else
                    {
                        throw ConstructParserException("Unexpected '}' in f-string. To output a brace, use '}}'.", _lexer.PeekCurrentToken());
                    }
                }
                else
                {
                    currentLiteral.Append(c);
                    i++;
                }
            }

            if (currentLiteral.Length > 0)
            {
                parts.Add(new StringValue(currentLiteral.ToString()));
            }

            if (parts.Count == 0)
            {
                return new StringValue("");
            }

            if (parts.Count == 1)
            {
                return parts[0];
            }

            Value finalResult = parts[0];
            for (int k = 1; k < parts.Count; k++)
            {
                finalResult = ConcatenateStringValues(finalResult, parts[k]);
            }

            return finalResult;
        }

        /// <summary>
        /// A helper to parse a sub-expression from a string, using a temporary, isolated lexer.
        /// </summary>
        private Value ParseSubExpression(string source)
        {
            FluenceLexer savedLexer = _lexer;
            try
            {
                _lexer = _lexerPool.Get();
                _lexer.Initialize(source);
                _lexer.LexFullSource();
                return ResolveValue(ParseTernary());
            }
            catch (FluenceException)
            {
                throw;
            }
            finally
            {
                _lexerPool.Return(_lexer);
                _lexer = savedLexer;
            }
        }

        /// <summary>
        /// Parses a dictionary literal expression.
        /// </summary>
        /// <returns>A TempValue that will hold the new dictionary instance at runtime.</returns>
        private TempValue ParseDictionary()
        {
            // '{' is already consumed.

            TempValue dictionary = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewDictionary, dictionary));

            if (!_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                ParseDictionaryKeyValuePair(dictionary);
            }

            AdvanceAndExpect(TokenType.R_BRACE, "Expected a closing '}' to end the dictionary literal.");
            return dictionary;
        }

        /// <summary>
        /// Parses a key-value pair for a dictionary declaration.
        /// </summary>
        /// <param name="dictionary">The dictionary the key-value pair will be added to.</param>
        private void ParseDictionaryKeyValuePair(TempValue dictionary)
        {
            do
            {
                Value key = ResolveValue(ParseExpression());

                AdvanceAndExpect(TokenType.THIN_ARROW, "Expected a '->' after the key of a key-value pair in a dictionary declaration.");

                Value value;

                // Dictionary as value.
                if (_lexer.PeekNextTokenType() == TokenType.L_BRACE)
                {
                    AdvanceTokenIfMatch(TokenType.L_BRACE);
                    value = ParseDictionary();
                }
                else
                {
                    value = ResolveValue(ParseExpression());
                }

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.PushKeyValuePair, dictionary, key, value));
            } while (AdvanceTokenIfMatch(TokenType.COMMA));
        }

        /// <summary>
        /// Parses a list literal expression.
        /// </summary>
        /// <returns>A TempValue that will hold the new list instance at runtime.</returns>
        private TempValue ParseList()
        {
            // '[' is already consumed.

            TempValue list = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewList, list));

            if (!_lexer.TokenTypeMatches(TokenType.R_BRACKET))
            {
                List<Value> elements = ParseTokenSeparatedArguments(TokenType.COMMA);

                foreach (Value element in elements)
                {
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.PushElement, list, ResolveValue(element)));
                }
            }

            AdvanceAndExpect(TokenType.R_BRACKET, "Expected a closing ']' to end the list literal.");
            return list;
        }

        /// <summary>
        /// Parses a `use` statement, which imports symbols from one or more namespaces.
        /// into the current scop
        private void ParseUseStatement()
        {
            AdvanceAndExpect(TokenType.USE, "Expected the 'use' keyword.");

            do
            {
                Token nameToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a namespace name after a 'use' statement.");
                string namespaceName = nameToken.Text;

                _intrinsicsManager.Use(namespaceName);

                if (!_currentParseState.NameSpaces.TryGetValue(namespaceName.GetHashCode(), out FluenceScope namespaceToUse))
                {
                    throw ConstructParserException($"Namespace '{namespaceName}' not found. Expected a defined namespace.", nameToken);
                }

                foreach (KeyValuePair<int, Symbol> entry in namespaceToUse.Symbols)
                {
                    if (!_currentParseState.GlobalScope.DeclaredSymbolNames.Contains(entry.Key) && !_currentParseState.GlobalScope.Declare(entry.Key, entry.Value))
                    {
                        throw ConstructParserException($"Symbol '{entry.Key}' from namespace '{namespaceName}' conflicts with a symbol already defined in this scope.", nameToken);
                    }
                }
            }
            while (AdvanceTokenIfMatch(TokenType.COMMA));

            AdvanceAndExpect(TokenType.EOL, "Expected a ';' to end the 'use' statement.");
        }

        /// <summary>
        /// Parses a block of statements enclosed in curly braces `{ ... }`.
        /// </summary>
        private void ParseBlockStatement()
        {
            AdvanceAndExpect(TokenType.L_BRACE, "Expected an opening '{' to start a block of code.");
            while (!_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                ParseStatement();
            }
            AdvanceAndExpect(TokenType.R_BRACE, "Expected a closing '}' to end a block of code.");
        }

        /// <summary>
        /// Parses a block of statements enclosed in custom 'end' and 'start' tokens. An imitation of a block of code.
        /// </summary>
        private void ParseImitationBlockStatement(TokenType openToken, TokenType closeToken)
        {
            AdvanceAndExpect(openToken, $"Expected an opening '{openToken}' to start an imitation block of code.");
            while (!_lexer.TokenTypeMatches(closeToken))
            {
                ParseStatement();
            }
            AdvanceAndExpect(closeToken, $"Expected a closing '{closeToken}' to end an imitation block of code.");
        }

        /// <summary>
        /// Parses the assignment of a solid variable, the variable must be assigned to an explicit value.
        /// </summary>
        private void ParseSolidStatement()
        {
            _lexer.Advance(); // Consume 'solid'.

            bool isRoot = false;

            // Can be "solid root var", root won't be caught by ParseStatement here.
            if (_lexer.PeekNextTokenType() == TokenType.ROOT)
            {
                _lexer.Advance();
                isRoot = true;
            }

            Value left = ParseExpression();

            if (left is not VariableValue)
            {
                throw ConstructParserException("Can not declare a constant value, or a non variable as solid.", _lexer.PeekCurrentToken());
            }

            VariableValue variable = (VariableValue)left;
            variable.IsReadOnly = true;
            variable.IsGlobal = isRoot;

            AdvanceAndExpect(TokenType.EQUAL, "Expected an assignment for an immutable solid variable or field.");

            Value value = ParseExpression();

            if (isRoot)
            {
                if (value is LambdaValue lambda)
                {
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewLambda, GetOrCreateVariable(Mangler.Mangle(variable.Name, lambda.Function.Arity)), value));
                    return;
                }

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.AssignIfNil, variable, value));
            }
            else
            {
                GenerateWriteBackInstruction(variable, value);
            }
        }

        /// <summary>
        /// Parses the assignment of a global aka root variable, the variable is marked as a global variable for the scope.
        /// </summary>
        private void ParseRootStatement()
        {
            _lexer.Advance(); // Consume 'root'.

            bool isSolid = false;

            // Can be "root solid var", solid won't be caught by ParseStatement here.
            if (_lexer.PeekNextTokenType() == TokenType.SOLID)
            {
                _lexer.Advance();
                isSolid = true;
            }

            Value left = ParseExpression();

            if (left is not VariableValue)
            {
                throw ConstructParserException("Can not declare a constant value, or a non variable as root ( global ).", _lexer.PeekCurrentToken());
            }

            VariableValue variable = (VariableValue)left;
            variable.IsGlobal = true;
            variable.IsReadOnly = isSolid;

            if (_lexer.PeekNextTokenType() == TokenType.EQUAL)
            {
                _lexer.Advance();
                Value value = ParseExpression();

                if (value is LambdaValue lambda)
                {
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewLambda, GetOrCreateVariable(Mangler.Mangle(variable.Name, lambda.Function.Arity)), value));
                    return;
                }

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.AssignIfNil, variable, value));
            }
            else
            {
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.AssignIfNil, variable, NilValue.NilInstance));
            }
        }

        /// <summary>
        /// Parses an assignment expression, which is the lowest level of precedence.
        /// This method acts as a dispatcher for all assignment-related syntax
        /// or handles a standalone expression used as a statement.
        /// </summary>
        private void ParseAssignment()
        {
            List<Value> lhsList = ParseLhs();
            Value firstLhs = lhsList[0];

            TokenType opType = _lexer.PeekNextTokenType();

            if ((IsSimpleAssignmentOperator(opType) || opType == TokenType.SWAP) && lhsList.Count > 1)
            {
                throw ConstructParserException("Simple assignment operators (=, +=, ><) cannot be used with a multi-variable list.", _lexer.PeekNextToken());
            }

            // Multi-Assign operators like .+=, .-= and so on.
            if (IsMultiCompoundAssignmentOperator(opType))
            {
                ParseMultiCompoundAssignment(lhsList);
                _lhsPool.Return(lhsList);
                return;
            }

            if (IsPipeCallAhead(out TokenType pipeType))
            {
                switch (pipeType)
                {
                    case TokenType.GUARD_PIPE:
                        ParseTruthyGuardChain(firstLhs);
                        break;
                    case TokenType.GUARD_CHAIN:
                    case TokenType.OR_GUARD_CHAIN:
                        ParseGuardChain(firstLhs);
                        break;
                }
                _lhsPool.Return(lhsList);
                return;
            }

            if (IsChainAssignmentOperator(opType))
            {
                if (opType is TokenType.SEQUENTIAL_REST_ASSIGN or TokenType.OPTIONAL_SEQUENTIAL_REST_ASSIGN)
                {
                    ParseSequentialRestAssign(lhsList);
                    _lhsPool.Return(lhsList);
                    return;
                }
                else if (opType is TokenType.OPTIONAL_CHAIN_N_UNIQUE_ASSIGN or TokenType.CHAIN_N_UNIQUE_ASSIGN or TokenType.UNIQUE_REST_ASSIGN)
                {
                    ParseUniqueChainAssignment(lhsList);
                    _lhsPool.Return(lhsList);
                    return;
                }

                ParseChainAssignment(lhsList);
                _lhsPool.Return(lhsList);
                return;
            }

            if (IsSimpleAssignmentOperator(opType) || opType == TokenType.SWAP)
            {
                TokenType type = _lexer.ConsumeToken().Type;
                Value rhs = ResolveValue(ParseTernary());

                if (type == TokenType.EQUAL)
                {
                    GenerateWriteBackInstruction(firstLhs, rhs);
                }
                else if (type == TokenType.SWAP)
                {
                    Value resolvedLhs = ResolveValue(firstLhs);
                    Value result = new TempValue(_currentParseState.NextTempNumber++);
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.AssignTwo, result, resolvedLhs, resolvedLhs, rhs));
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, rhs, result));
                }
                else  // Compound, -=, +=, etc.
                {
                    Value resolvedRhs = ResolveValue(firstLhs);
                    InstructionCode opCode = GetInstructionCodeForBinaryOperator(type);

                    _currentParseState.AddCodeInstruction(new InstructionLine(opCode, resolvedRhs, resolvedRhs, rhs));

                    if (firstLhs is PropertyAccessValue)
                    {
                        GenerateWriteBackInstruction(firstLhs, resolvedRhs);
                    }

                    if (firstLhs is VariableValue variable && !_currentParseState.IsParsingFunctionBody)
                    {
                        _currentParseState.CurrentScope.Declare(variable.Hash, new VariableSymbol(variable.Name, firstLhs));
                    }
                }
            }
            else
            {
                // In Fluence the statement 'variable;' is valid, but it would be ignored.
                if (firstLhs is StatementCompleteValue or ElementAccessValue)
                {
                    // Either a StatementCompleteValue and we do nothing.
                    // Or some nonsense like:
                    // list[0]; Not a write, but reading is pointless.
                    _lhsPool.Return(lhsList);
                    return;
                }
                else if (firstLhs is VariableValue variable)
                {
                    // The expression was just a variable. Force a read.
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, variable, NilValue.NilInstance));
                }
            }
        }

        /// <summary>
        /// Parses a multi-target compound assignment expression.
        /// </summary>
        /// <param name="lhsDescriptors">The list of left-hand side targets to be modified.</param>
        private void ParseMultiCompoundAssignment(List<Value> lhsDescriptors)
        {
            Token opToken = _lexer.ConsumeToken();

            InstructionCode operation = GetInstructionCodeForMultiCompoundAssignment(opToken.Type);

            List<Value> rhsExpressions = ParseTokenSeparatedArguments(TokenType.COMMA);

            if (lhsDescriptors.Count != rhsExpressions.Count)
            {
                throw ConstructParserException($"Mismatched number of items for multi-compound assignment '{opToken.ToDisplayString()}'.", opToken);
            }

            for (int i = 0; i < lhsDescriptors.Count; i++)
            {
                Value targetDescriptor = lhsDescriptors[i];
                Value rhsValue = ResolveValue(rhsExpressions[i]);
                Value lhsValue = ResolveValue(targetDescriptor);

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(operation, result, lhsValue, rhsValue));

                GenerateWriteBackInstruction(targetDescriptor, result);
            }
        }

        /// <summary>
        /// Parses the left-hand side of a potential assignment expression.
        /// </summary>
        /// <returns>A list of Value objects representing the parsed LHS.</returns>
        private List<Value> ParseLhs()
        {
            if (IsBroadCastPipeFunctionCall())
            {
                List<Value> list = _lhsPool.Get();
                list.Add(ParseBroadcastCallTemplate());
                return list;
            }

            return ParseTokenSeparatedArguments(TokenType.COMMA);
        }

        /// <summary>
        /// Checks if special pipe call operator is up ahead in the expression.
        /// </summary>
        /// <param name="pipeType">Returns the type of the pipe if true, else <see cref="TokenType.UNKNOWN"/>.</param>
        /// <returns>True if a special pipe operator is ahead.</returns>
        private bool IsPipeCallAhead(out TokenType pipeType)
        {
            int lookahead = 1;

            while (true)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(lookahead);

                if (type == TokenType.EOL)
                {
                    pipeType = TokenType.UNKNOWN;
                    return false;
                }

                if (type is TokenType.GUARD_PIPE or TokenType.GUARD_CHAIN or TokenType.OR_GUARD_CHAIN)
                {
                    pipeType = type;
                    return true;
                }

                if (type == TokenType.EOF)
                {
                    pipeType = TokenType.UNKNOWN;
                    return false;
                }
                lookahead++;
            }
        }

        /// <summary>
        /// Checks if a chain assignment operator is up ahead in the expression.
        /// </summary>
        /// <param name="pipeType">Returns the type of the pipe if true, else <see cref="TokenType.UNKNOWN"/>.</param>
        /// <returns>True if a special chain assignment operator is ahead.</returns>
        private bool IsChainAssignmentAhead(out TokenType pipeType)
        {
            int lookahead = 1;

            while (true)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(lookahead);

                if (type == TokenType.EOL)
                {
                    pipeType = TokenType.UNKNOWN;
                    return false;
                }

                if (type is >= TokenType.CHAIN_ASSIGN_N and <= TokenType.OPTIONAL_SEQUENTIAL_REST_ASSIGN)
                {
                    pipeType = type;
                    return true;
                }

                if (type == TokenType.EOF)
                {
                    pipeType = TokenType.UNKNOWN;
                    return false;
                }
                lookahead++;
            }
        }

        /// <summary>
        /// A helper method to parse a broadcast call template.
        /// </summary>
        /// <returns>A BroadcastCallTemplate object representing the parsed template.</returns>
        private BroadcastCallTemplate ParseBroadcastCallTemplate()
        {
            Value functionToCall;

            if (_lexer.PeekTokenTypeAheadByN(2) == TokenType.DOT)
            {
                string name = _lexer.ConsumeToken().Text;

                if (_currentParseState.CurrentScope.TryResolve(name.GetHashCode(), out Symbol symbol) && symbol is StructSymbol structSymbol)
                {
                    _lexer.Advance();
                    functionToCall = new StaticStructAccess(structSymbol, _lexer.ConsumeToken().Text);
                }
                else
                {
                    _lexer.Advance();
                    functionToCall = new PropertyAccessValue(GetOrCreateVariable(name), _lexer.ConsumeToken().Text);
                }
            }
            else
            {
                functionToCall = ParsePrimary();
            }

            Token openingParen = ConsumeAndExpect(TokenType.L_PAREN, "Expected an opening '(' for the broadcast call template.");

            List<Value> args = new List<Value>();
            int underscoreIndex = -1;

            do
            {
                if (_lexer.TokenTypeMatches(TokenType.UNDERSCORE))
                {
                    Token token = _lexer.ConsumeToken();
                    if (underscoreIndex != -1)
                    {
                        throw ConstructParserExceptionWithUnexpectedToken("Cannot use more than one `_` placeholder in a broadcast call.", token);
                    }
                    underscoreIndex = args.Count;
                    args.Add(NilValue.NilInstance);
                }
                else
                {
                    args.Add(ParseTernary());
                }
            }
            while (AdvanceTokenIfMatch(TokenType.COMMA));

            AdvanceAndExpect(TokenType.R_PAREN, "Expected a closing ')' for the broadcast call template.");

            // Semantic check: A broadcast template must contain a placeholder.
            if (underscoreIndex == -1)
            {
                throw ConstructParserException("Broadcast call template must contain a `_` placeholder.", openingParen);
            }

            return new BroadcastCallTemplate(functionToCall!, args, underscoreIndex);
        }

        /// <summary>
        /// Parses a guard chain, or its or variant: <??| or <||??|.
        /// </summary>
        /// <param name="lhs">The variable on the left to which false or true is assigned.</param>
        private void ParseGuardChain(Value lhs)
        {
            Token pipe = _lexer.ConsumeToken();

            bool isOptional = pipe.Type == TokenType.OR_GUARD_CHAIN;

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, lhs, new BooleanValue(!isOptional)));

            List<Value> expressions = ParseTokenSeparatedArguments(TokenType.COMMA);
            List<int> boolyExitPatches = new List<int>(expressions.Count);

            for (int i = 0; i < expressions.Count; i++)
            {
                Value curr = expressions[i];

                TempValue temp = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, temp, curr, BooleanValue.True));
                _currentParseState.AddCodeInstruction(new InstructionLine(isOptional ? InstructionCode.GotoIfTrue : InstructionCode.GotoIfFalse, null!, temp));
                boolyExitPatches.Add(_currentParseState.CodeInstructions.Count - 1);
            }

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int breakIndex = _currentParseState.CodeInstructions.Count - 1;

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, lhs, new BooleanValue(isOptional)));
            int exitIndex = _currentParseState.CodeInstructions.Count - 1;

            for (int i = 0; i < boolyExitPatches.Count; i++)
            {
                _currentParseState.CodeInstructions[boolyExitPatches[i]].Lhs = new GoToValue(exitIndex);
            }

            _currentParseState.CodeInstructions[breakIndex].Lhs = new GoToValue(exitIndex + 1);
        }

        /// <summary>
        /// Parses a pipeline of |?? truthy expressions. Returns false if even one of the expressions is false.
        /// Otherwise assigns true.
        /// </summary>
        /// <param name="lhs">The variable on the left to which false or true is assigned.</param>
        private void ParseTruthyGuardChain(Value lhs)
        {
            _lexer.Advance(); // Consume first '|??'.

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, lhs, BooleanValue.True));

            List<Value> expressions = ParseTokenSeparatedArguments(TokenType.GUARD_PIPE);
            List<int> boolyExitPatches = new List<int>(expressions.Count);

            for (int i = 0; i < expressions.Count; i++)
            {
                Value curr = expressions[i];

                TempValue temp = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, temp, curr, BooleanValue.True));
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, temp));
                boolyExitPatches.Add(_currentParseState.CodeInstructions.Count - 1);
            }

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int breakIndex = _currentParseState.CodeInstructions.Count - 1;

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, lhs, BooleanValue.False));
            int exitIndex = _currentParseState.CodeInstructions.Count - 1;

            for (int i = 0; i < boolyExitPatches.Count; i++)
            {
                _currentParseState.CodeInstructions[boolyExitPatches[i]].Lhs = new GoToValue(exitIndex);
            }

            _currentParseState.CodeInstructions[breakIndex].Lhs = new GoToValue(exitIndex + 1);
        }

        /// <summary>
        /// Parses a sequential assignment expression, both optional and not.
        /// </summary>
        /// <param name="lhsDescriptors">A list of the left-hand side variables or descriptors to be assigned to.</param>
        private void ParseSequentialRestAssign(List<Value> lhsDescriptors)
        {
            int lhsIndex = 0;
            Token opToken = _lexer.ConsumeToken(); // Consume '<~|'.
            bool isOptional = opToken.Type == TokenType.OPTIONAL_SEQUENTIAL_REST_ASSIGN;

            do
            {
                if (lhsIndex >= lhsDescriptors.Count)
                {
                    throw ConstructParserException($"Too many values on the right side of sequential assignment '{opToken.Text}'. Expected {lhsDescriptors.Count}.", _lexer.PeekCurrentToken());
                }

                Value rhs = ResolveValue(ParseTernary());

                int skipOptionalAssign = -1;
                if (isOptional)
                {
                    TempValue isNil = new TempValue(_currentParseState.NextTempNumber++);
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, isNil, rhs, NilValue.NilInstance));
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, null!, isNil));
                    skipOptionalAssign = _currentParseState.CodeInstructions.Count - 1;
                }

                GenerateWriteBackInstruction(lhsDescriptors[lhsIndex], rhs);

                if (skipOptionalAssign != -1)
                {
                    _currentParseState.CodeInstructions[skipOptionalAssign].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
                }

                lhsIndex++;

            } while (AdvanceTokenIfMatch(TokenType.COMMA));

            if (lhsIndex != lhsDescriptors.Count)
            {
                throw ConstructParserException($"Mismatched number of items for sequential assignment. Expected {lhsDescriptors.Count}, got {lhsIndex}.", opToken);
            }
        }

        /// <summary>
        /// Parses a chain assignment expression.
        /// </summary>
        /// <param name="lhs">The already-parsed left-hand side, which determines the type of chain.</param>
        private void ParseChainAssignment(List<Value> lhs)
        {
            if (lhs.Count == 1 && lhs[0] is BroadcastCallTemplate broadcastCall)
            {
                ParseBroadCastCallChain(broadcastCall);
            }
            else
            {
                ParseStandardChainAssignment(lhs);
            }
        }

        /// <summary>
        /// Parses a chain assinment of values, but allows them to be unique for each variable.
        /// This means that the expression is evaluated N times.
        /// </summary>
        private void ParseUniqueChainAssignment(List<Value> lhsExpressions)
        {
            int lhsIndex = 0;

            while (IsChainAssignmentOperator(_lexer.PeekNextTokenType()))
            {
                if (lhsIndex >= lhsExpressions.Count)
                {
                    // All variables have been assigned, but there are more operators.
                    throw ConstructParserException("Redundant chain assignment operator. No more variables are available for assignment.", _lexer.PeekNextToken());
                }

                Token op = _lexer.ConsumeToken();

                bool isOptional = IsOptionalChainAssignmentOperator(op.Type);
                List<int> optionalSkipIndexes = new List<int>();

                bool isUniqueRestAssignment = op.Type == TokenType.UNIQUE_REST_ASSIGN;

                if (op.Type is TokenType.OPTIONAL_CHAIN_N_UNIQUE_ASSIGN or TokenType.CHAIN_N_UNIQUE_ASSIGN || isUniqueRestAssignment)
                {
                    int count = isUniqueRestAssignment ? lhsExpressions.Count : int.Parse((string)op.Literal);

                    int start = _currentParseState.CodeInstructions.Count;
                    Value rhs = ParseTernary();
                    int end = _currentParseState.CodeInstructions.Count;
                    bool ignoreFirstCopy = true;

                    for (int i = 0; i < count; i++)
                    {
                        if (!ignoreFirstCopy)
                        {
                            _currentParseState.CodeInstructions.AddRange(_currentParseState.CodeInstructions.GetRange(start, end - start));
                        }
                        ignoreFirstCopy = false;

                        TempValue valueToAssign = new TempValue(_currentParseState.NextTempNumber);

                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, valueToAssign, rhs));

                        if (isOptional)
                        {
                            TempValue isNil = new TempValue(_currentParseState.NextTempNumber + 1);
                            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, isNil, valueToAssign, NilValue.NilInstance));
                            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, null!, isNil));
                            optionalSkipIndexes.Add(_currentParseState.CodeInstructions.Count - 1);
                        }

                        if (lhsIndex >= lhsExpressions.Count)
                        {
                            throw ConstructParserException($"Chain operator '{op.ToDisplayString()}' expected {count} variables, but only {i} were available. There are more variables on the left-hand side.", op);
                        }

                        GenerateWriteBackInstruction(lhsExpressions[lhsIndex], valueToAssign);
                        lhsIndex++;
                    }

                    if (optionalSkipIndexes.Count != 0)
                    {
                        for (int i = 0; i < optionalSkipIndexes.Count; i++)
                        {
                            _currentParseState.CodeInstructions[optionalSkipIndexes[i]].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
                        }
                        optionalSkipIndexes.Clear();
                    }
                }
                else
                {
                    while (lhsIndex < lhsExpressions.Count)
                    {
                        GenerateWriteBackInstruction(lhsExpressions[lhsIndex], ParseTernary());
                        lhsIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// Parses a standard chain assignment.
        /// </summary>
        private void ParseStandardChainAssignment(List<Value> lhsExpressions)
        {
            int lhsIndex = 0;
            while (IsChainAssignmentOperator(_lexer.PeekNextTokenType()))
            {
                if (lhsIndex >= lhsExpressions.Count)
                {
                    // All variables have been assigned, but there are more operators.
                    throw ConstructParserException("Redundant chain assignment operator. No more variables are available for assignment.", _lexer.PeekNextToken());
                }

                Token op = _lexer.ConsumeToken();

                bool isOptional = IsOptionalChainAssignmentOperator(op.Type);

                Value rhs = ParseTernary();

                TempValue valueToAssign = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, valueToAssign, rhs));

                int skipOptionalAssign = -1;
                if (isOptional)
                {
                    TempValue isNil = new TempValue(_currentParseState.NextTempNumber++);
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, isNil, valueToAssign, NilValue.NilInstance));
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, null!, isNil));
                    skipOptionalAssign = _currentParseState.CodeInstructions.Count - 1;
                }

                if (op.Type is TokenType.CHAIN_ASSIGN_N or TokenType.OPTIONAL_ASSIGN_N)
                {
                    int count = Convert.ToInt32(op.Literal);
                    for (int i = 0; i < count; i++)
                    {
                        if (lhsIndex >= lhsExpressions.Count)
                        {
                            throw ConstructParserException($"Chain operator '{op.ToDisplayString()}' expected {count} variables, but only {i} were available. There are more variables on the left-hand side.", op);
                        }
                        GenerateWriteBackInstruction(lhsExpressions[lhsIndex], valueToAssign);
                        lhsIndex++;
                    }
                }
                else
                {
                    // Rest assignment here "<|".
                    while (lhsIndex < lhsExpressions.Count)
                    {
                        GenerateWriteBackInstruction(lhsExpressions[lhsIndex], valueToAssign);
                        lhsIndex++;
                    }
                }

                if (skipOptionalAssign != -1)
                {
                    _currentParseState.CodeInstructions[skipOptionalAssign].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
                }
            }
        }

        /// <summary>
        /// Helper to parse a broadcast call chain.
        /// </summary>
        private void ParseBroadCastCallChain(BroadcastCallTemplate broadcastCall)
        {
            // Although weird, there are cases when you could pipe multiple broadcast calls.
            while (IsChainAssignmentOperator(_lexer.PeekNextTokenType()))
            {
                Token op = _lexer.ConsumeToken(); // Consume '<|' or '<?|'.

                if (op.Type is not TokenType.REST_ASSIGN and not TokenType.OPTIONAL_REST_ASSIGN)
                {
                    throw ConstructParserExceptionWithUnexpectedToken("Invalid operator for broadcast call. Expected '<|' or '<?|'.", op);
                }

                bool isOptional = op.Type == TokenType.OPTIONAL_REST_ASSIGN;

                List<Value> rhsExpressions = ParseTokenSeparatedArguments(TokenType.COMMA);

                if (rhsExpressions.Count == 0)
                {
                    throw ConstructParserException("Broadcast call expects at least one value on the right-hand side. Expected one or more comma-separated values.", op);
                }

                foreach (Value rhsValue in rhsExpressions)
                {
                    Value resolvedRhs = ResolveValue(rhsValue);

                    int skipOptionalAssign = -1;
                    if (isOptional)
                    {
                        TempValue isNil = new TempValue(_currentParseState.NextTempNumber++);
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, isNil, resolvedRhs, NilValue.NilInstance));
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, null!, isNil));
                        skipOptionalAssign = _currentParseState.CodeInstructions.Count - 1;
                    }

                    broadcastCall.Arguments[broadcastCall.PlaceholderIndex] = resolvedRhs;

                    foreach (Value item in broadcastCall.Arguments)
                    {
                        _currentParseState.AddCodeInstruction(new InstructionLine(item is ReferenceValue ? InstructionCode.LoadAddress : InstructionCode.PushParam, item));
                    }

                    TempValue temp = new TempValue(_currentParseState.NextTempNumber++);

                    if (broadcastCall.Callable is StaticStructAccess statAccess)
                    {
                        _currentParseState.AddCodeInstruction(new InstructionLine(
                            InstructionCode.CallStatic,
                            temp,
                            statAccess.Struct,
                            new StringValue(Mangler.Mangle(statAccess.Name, broadcastCall.Arguments.Count))
                        ));
                    }
                    else if (broadcastCall.Callable is PropertyAccessValue propAccess)
                    {
                        _currentParseState.AddCodeInstruction(new InstructionLine(
                            InstructionCode.CallMethod,
                            temp,
                            propAccess.Target,
                            new StringValue(Mangler.Mangle(propAccess.FieldName, broadcastCall.Arguments.Count))
                        ));
                    }
                    else
                    {
                        VariableValue var = (VariableValue)broadcastCall.Callable;
                        VariableValue mangle = GetOrCreateVariable(Mangler.Mangle(var.Name, broadcastCall.Arguments.Count));
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.CallFunction, temp, mangle, new NumberValue(broadcastCall.Arguments.Count)));
                    }

                    if (skipOptionalAssign != -1)
                    {
                        _currentParseState.CodeInstructions[skipOptionalAssign].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
                    }
                }
            }
        }

        /// <summary>
        /// Parses a ternary expression or a Fluence-style joint ternary.
        /// </summary>
        private Value ParseTernary()
        {
            // If Ternary, this becomes the condition.
            Value left = ParseNullCoalescing();

            TokenType type = _lexer.PeekNextTokenType();

            if (type is not TokenType.QUESTION and not TokenType.TERNARY_JOINT)
            {
                return left;
            }

            // Two formats, normal: cond ? a : b
            // Joint: cond ?: a, b

            _lexer.Advance(); // Consume '?' or '?:'

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, left));
            int falseJumpPatch = _currentParseState.CodeInstructions.Count - 1;

            Value trueExpr = ResolveValue(ParseTernary());

            TempValue result = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, result, trueExpr));

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int endJumpPatch = _currentParseState.CodeInstructions.Count - 1;

            int falsePathAddress = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[falseJumpPatch].Lhs = new GoToValue(falsePathAddress);

            // Consume the ':' or ',' delimiter.
            if (type == TokenType.QUESTION)
            {
                AdvanceAndExpect(TokenType.COLON, "Expected a ':' in the ternary expression.");
            }
            else
            {
                AdvanceAndExpect(TokenType.COMMA, "Expected ',' in a Fluid-style ternary expression.");
            }

            Value falseExpr = ResolveValue(ParseTernary());
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, result, falseExpr));

            int endAddress = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[endJumpPatch].Lhs = new GoToValue(endAddress);

            // The "value" of this entire ternary expression for the rest of the parser
            // is the temporary variable that holds the chosen result.
            return result;
        }

        /// <summary>
        /// Parses a null-coalescing expression.
        /// </summary>
        private Value ParseNullCoalescing()
        {
            Value left = ParsePipe();

            while (_lexer.PeekNextTokenType() == TokenType.NULL_COALESCING)
            {
                _lexer.ConsumeToken();

                Value fallbackValue = ResolveValue(ParsePipe());

                TempValue isNull = new TempValue(_currentParseState.NextTempNumber++);
                TempValue returnValue = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, returnValue, left));
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Equal, isNull, left, NilValue.NilInstance));

                int gotoIndex = _currentParseState.CodeInstructions.Count;
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfFalse, null!, isNull));

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, returnValue, fallbackValue));

                _currentParseState.CodeInstructions[gotoIndex].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);

                left = returnValue;
            }

            return left;
        }

        /// <summary>
        /// Dispatches the parsing of '|>' operator or a chain of it.
        /// </summary>
        private Value ParsePipe()
        {
            Value left = ParseExpression();

            // While we see a pipe, parse it and try again.
            bool first = true;
            while (_lexer.PeekNextTokenType() == TokenType.PIPE)
            {
                left = ParsePipe(left, first);
                first = false;
            }

            return left;
        }

        /// <summary>
        /// Parses a chain of '|>' pipe operators.
        /// </summary>
        private Value ParsePipe(Value left, bool first)
        {
            while (true)
            {
                TokenType pipeType = _lexer.PeekNextTokenType();

                if (pipeType == TokenType.PIPE)
                {
                    if (_lexer.PeekTokenTypeAheadByN(2) == TokenType.DOT)
                    {
                        _lexer.AdvanceMany(2); // Consume '|>' and '.'
                        // This is a method pipe call, like `|>.append()`
                        left = ParseMethodPipeCall(left);
                    }
                    else
                    {
                        // This is a standard pipe call, like `|> printl()`
                        _lexer.Advance(); // Consume `|>`.
                        left = ParseStandardPipeCall(left, first);
                    }
                }
                else
                {
                    // No more pipe operators, the chain is finished.
                    break;
                }
            }

            return left;
        }

        /// <summary>
        /// Parses a '|>>=' reducer pipe
        /// </summary>
        /// <param name="collection">The collection to reduce.</param>
        /// <returns>The reduced result.</returns>
        private TempValue ParseReducerPipe(Value collection)
        {
            _lexer.Advance(); // Consume '|>>='.
            AdvanceAndExpect(TokenType.L_PAREN, "Expected an opening '(' for Reducer Pipe arguments.");

            Value initialValue = ResolveValue(ParseExpression());
            AdvanceAndExpect(TokenType.COMMA, "Expected a comma between initial value and lambda in Reducer Pipe.");
            AdvanceAndExpect(TokenType.L_PAREN, "Expected an opening '(' for Reducer Pipe lambda.");

            LambdaValue lambda = ParseLambda();

            // Block body lambda.
            if (_lexer.PeekNextTokenType() == TokenType.EOL)
            {
                _lexer.Advance();
            }
            if (lambda.Function.Arity != 2)
            {
                throw ConstructParserException($"Reducer pipe lambda must take exactly 2 arguments (accumulator, element), but got {lambda.Function.Arity} arguments.", _lexer.PeekNextToken());
            }

            AdvanceAndExpect(TokenType.R_PAREN, "Expected a closing ')' for Reducer Pipe arguments.");

            TempValue accumulatorRegister = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, accumulatorRegister, initialValue));

            TempValue iteratorRegister = new TempValue(_currentParseState.NextTempNumber++);
            TempValue elementRegister = new TempValue(_currentParseState.NextTempNumber++);
            TempValue continueFlagRegister = new TempValue(_currentParseState.NextTempNumber++);

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewIterator, iteratorRegister, collection));
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int gotoEndIndex = _currentParseState.CodeInstructions.Count - 1;

            int loopBodyStartIndex = _currentParseState.CodeInstructions.Count;
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.PushTwoParams, accumulatorRegister, elementRegister));
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.CallFunction, accumulatorRegister, lambda, new NumberValue(2)));

            int conditionStartIndex = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[gotoEndIndex].Lhs = new GoToValue(conditionStartIndex);

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.IterNext, iteratorRegister, elementRegister, continueFlagRegister));
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, new GoToValue(loopBodyStartIndex), continueFlagRegister));

            return accumulatorRegister;
        }

        /// <summary>
        /// Parses a method call on the right-hand side of a method pipe `|>.`.
        /// Assumes the `|>` and `.` tokens have already been consumed.
        /// </summary>
        /// <param name="pipedObject">The result of the previous expression (the object to call the method on).</param>
        private TempValue ParseMethodPipeCall(Value pipedObject)
        {
            Token methodNameToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a method name after '|>.'");
            string methodName = methodNameToken.Text;

            AdvanceAndExpect(TokenType.L_PAREN, "Expected '(' to begin method arguments.");
            List<Value> arguments = ParseArgumentList();
            AdvanceAndExpect(TokenType.R_PAREN, "Expected ')' to close method arguments.");

            foreach (Value arg in arguments)
            {
                _currentParseState.AddCodeInstruction(new InstructionLine(arg is ReferenceValue ? InstructionCode.LoadAddress : InstructionCode.PushParam, ResolveValue(arg)));
            }

            TempValue result = new TempValue(_currentParseState.NextTempNumber++);
            string mangledMethodName = Mangler.Mangle(methodName, arguments.Count);

            _lhsPool.Return(arguments);

            _currentParseState.AddCodeInstruction(new InstructionLine(
                InstructionCode.CallMethod,
                result,
                pipedObject,
                new StringValue(mangledMethodName)
            ));

            return result;
        }

        /// <summary>
        /// Parses the right-hand side of a pipe expression, which must be a function call.
        /// It finds the `_` placeholder and injects the piped value.
        /// </summary>
        /// <param name="leftSidePipedValue">The value from the left-hand side of the pipe.</param>
        private TempValue ParseStandardPipeCall(Value leftSidePipedValue, bool firstArg)
        {
            Value targetFunction = ParsePrimary();
            AdvanceAndExpect(TokenType.L_PAREN, "Expected a function call with `(` after a pipe `|>` operator.");

            List<Value> args = new List<Value>();
            bool foundUnderscore = false;

            while (AdvanceTokenIfMatch(TokenType.COMMA) || !_lexer.TokenTypeMatches(TokenType.R_PAREN))
            {
                if (_lexer.TokenTypeMatches(TokenType.UNDERSCORE))
                {
                    foundUnderscore = true;
                    _lexer.Advance();
                    args.Add(leftSidePipedValue);
                }
                else
                {
                    args.Add(ParsePipe());
                }
            }

            if (!foundUnderscore && !firstArg)
            {
                throw ConstructParserException("Only the first expression in a pipe call is allowed to not have the '_' argument, the rest must have it.", _lexer.PeekNextToken());
            }

            AdvanceAndExpect(TokenType.R_PAREN, "Expected a closing ')' for the function call in a pipe.");

            foreach (Value arg in args)
            {
                _currentParseState.AddCodeInstruction(new InstructionLine(arg is ReferenceValue ? InstructionCode.LoadAddress : InstructionCode.PushParam, arg));
            }

            TempValue result = new TempValue(_currentParseState.NextTempNumber++);
            VariableValue var = (VariableValue)targetFunction;
            VariableValue mangle = GetOrCreateVariable(Mangler.Mangle(var.Name, args.Count));
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.CallFunction, result, mangle, new NumberValue(args.Count)));

            return result;
        }

        /// <summary>
        /// Converts a binary operator TokenType into its corresponding InstructionCode.
        /// Handles arithmetic, comparison, logical, and bitwise operators.
        /// </summary>
        /// <param name="type">The TokenType of the binary operator.</param>
        /// <returns>The corresponding InstructionCode.</returns>
        private static InstructionCode GetInstructionCodeForBinaryOperator(TokenType type) => type switch
        {
            // Arithmetic.
            TokenType.PLUS => InstructionCode.Add,
            TokenType.MINUS => InstructionCode.Subtract,
            TokenType.STAR => InstructionCode.Multiply,
            TokenType.SLASH => InstructionCode.Divide,
            TokenType.PERCENT => InstructionCode.Modulo,
            TokenType.EXPONENT => InstructionCode.Power,

            TokenType.EQUAL_EQUAL => InstructionCode.Equal,
            TokenType.BANG_EQUAL => InstructionCode.NotEqual,
            TokenType.GREATER => InstructionCode.GreaterThan,
            TokenType.LESS => InstructionCode.LessThan,
            TokenType.GREATER_EQUAL => InstructionCode.GreaterEqual,
            TokenType.LESS_EQUAL => InstructionCode.LessEqual,

            TokenType.EQUAL_PLUS => InstructionCode.Add,
            TokenType.EQUAL_MINUS => InstructionCode.Subtract,
            TokenType.EQUAL_DIV => InstructionCode.Divide,
            TokenType.EQUAL_MUL => InstructionCode.Multiply,
            TokenType.EQUAL_AMPERSAND => InstructionCode.BitwiseAnd,
            TokenType.EQUAL_PERCENT => InstructionCode.Modulo,

            TokenType.AND => InstructionCode.And,
            TokenType.OR => InstructionCode.Or,

            // Bitwise.
            TokenType.BITWISE_LEFT_SHIFT => InstructionCode.BitwiseLShift,
            TokenType.BITWISE_RIGHT_SHIFT => InstructionCode.BitwiseRShift,
            TokenType.CARET => InstructionCode.BitwiseXor,
            TokenType.PIPE_CHAR => InstructionCode.BitwiseOr,
            TokenType.AMPERSAND => InstructionCode.BitwiseAnd,

            _ => throw new ArgumentException($"Token type '{type}' is not a recognized binary operator.", nameof(type))
        };

        /// <summary>
        /// The main entry point for parsing any expression.
        /// It begins the chain of precedence by calling <see cref="ParseReducerPipe"/>.
        /// </summary>
        private Value ParseExpression() => ParseReducerPipe();

        /// <summary>
        /// Parses a reducer pipe '|>>=' if there is one.
        /// </summary>
        private Value ParseReducerPipe()
        {
            Value left = ParseLogicalOr();

            while (_lexer.TokenTypeMatches(TokenType.REDUCER_PIPE))
            {
                left = ParseReducerPipe(ResolveValue(left));
            }

            return left;
        }

        /// <summary>
        /// Parses logical OR expressions '||'.
        /// </summary>
        private Value ParseLogicalOr()
        {
            Value left = ParseLogicalAnd();

            while (_lexer.TokenTypeMatches(TokenType.OR))
            {
                _lexer.Advance();
                Value right = ParseLogicalAnd();

                Value result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Or, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses logical AND expressions (&&).
        /// </summary>
        private Value ParseLogicalAnd()
        {
            Value left = ParseBitwiseOr();

            while (_lexer.TokenTypeMatches(TokenType.AND))
            {
                _lexer.Advance();
                Value right = ParseBitwiseOr();

                Value result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.And, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses bitwise OR expressions (|).
        /// </summary>
        private Value ParseBitwiseOr()
        {
            Value left = ParseBitwiseXor();

            // | is called PIPE_CHAR.
            while (_lexer.TokenTypeMatches(TokenType.PIPE_CHAR))
            {
                _lexer.Advance();
                Value right = ParseBitwiseXor();

                Value result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.BitwiseOr, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses bitwise XOR expressions (^).
        /// </summary>
        private Value ParseBitwiseXor()
        {
            Value left = ParseBitwiseAnd();

            while (_lexer.TokenTypeMatches(TokenType.CARET))
            {
                _lexer.Advance();
                Value right = ParseBitwiseAnd();

                Value result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.BitwiseXor, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses bitwise AND expressions (&).
        /// </summary>
        private Value ParseBitwiseAnd()
        {
            Value left = ParseEquality();

            while (_lexer.TokenTypeMatches(TokenType.AMPERSAND))
            {
                _lexer.Advance();
                Value right = ParseBitwiseAnd();

                Value result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.BitwiseAnd, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses equality expressions.
        /// </summary>
        private Value ParseEquality()
        {
            Value left = ParseBitwiseShift();

            while (_lexer.TokenTypeMatches(TokenType.EQUAL_EQUAL) || _lexer.TokenTypeMatches(TokenType.BANG_EQUAL))
            {
                Token op = _lexer.ConsumeToken();
                Value right = ParseBitwiseShift();

                Value result = new TempValue(_currentParseState.NextTempNumber++);
                InstructionCode opcode = (op.Type == TokenType.EQUAL_EQUAL)
                    ? InstructionCode.Equal
                    : InstructionCode.NotEqual;

                _currentParseState.AddCodeInstruction(new InstructionLine(opcode, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses bitwise shift expressions (<<, >>).
        /// </summary>
        private Value ParseBitwiseShift()
        {
            Value left = ParseComparison();

            while (_lexer.TokenTypeMatches(TokenType.BITWISE_LEFT_SHIFT) || _lexer.TokenTypeMatches(TokenType.BITWISE_RIGHT_SHIFT))
            {
                Token op = _lexer.ConsumeToken();
                Value right = ParseComparison();

                Value result = new TempValue(_currentParseState.NextTempNumber++);
                InstructionCode opcode = (op.Type == TokenType.BITWISE_LEFT_SHIFT)
                    ? InstructionCode.BitwiseLShift
                    : InstructionCode.BitwiseRShift;

                _currentParseState.AddCodeInstruction(new InstructionLine(opcode, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses comparison expressions. This is a complex precedence level that handles three forms of syntax:
        /// 1. Dot-prefixed logical operators (`.and(...)`, `.or(...)`).
        /// 2. Collective comparisons.
        /// 3. Standard binary comparisons.
        /// </summary>
        private Value ParseComparison()
        {
            TokenType opType = _lexer.PeekNextTokenType();

            // The .and()/.or() syntax must be checked first, as it doesn't follow the infix `left op right` pattern.
            if (opType is TokenType.DOT_AND_CHECK or TokenType.DOT_OR_CHECK)
            {
                return ParseDotAndOrOperators();
            }

            Value left = ParseRange();

            // Potential collective comparison.
            if (_lexer.TokenTypeMatches(TokenType.COMMA) && IsCollectiveComparisonAhead())
            {
                List<Value> args = new List<Value>() { left };
                _lexer.Advance();

                do
                {
                    args.Add(ParseRange());
                } while (AdvanceTokenIfMatch(TokenType.COMMA) && IsNotAStandardComparison(_lexer.PeekNextTokenType()));

                return GenerateCollectiveComparisonByteCode(args, _lexer.ConsumeToken(), ParseRange());
            }

            while (IsStandardComparisonOperator(_lexer.PeekNextTokenType()))
            {
                Token op = _lexer.ConsumeToken();
                Value right = ParseRange();

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(GetInstructionCodeForBinaryOperator(op.Type), result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses a short-circuiting logical check using `.and(...)` or `.or(...)` syntax.
        /// </summary>
        /// <returns>A TempValue that will hold the final boolean result at runtime.</returns>
        private Value ParseDotAndOrOperators()
        {
            Token opToken = _lexer.ConsumeToken(); // Consume '.and' or '.or'.

            AdvanceAndExpect(TokenType.L_PAREN, $"Expected an opening '(' after '{opToken.ToDisplayString()}'.");

            InstructionCode logicalOp = opToken.Type == TokenType.DOT_AND_CHECK ? InstructionCode.And : InstructionCode.Or;

            // Empty call case.
            if (_lexer.TokenTypeMatches(TokenType.R_PAREN))
            {
                throw ConstructParserException("Argument list for .and()/.or() can not be empty. Expected at least one boolean expression.", opToken);
            }

            Value result = ResolveValue(ParseExpression());

            while (AdvanceTokenIfMatch(TokenType.COMMA))
            {
                Value nextCondition = ResolveValue(ParseExpression());

                TempValue combinedResult = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(logicalOp, combinedResult, result, nextCondition));

                result = combinedResult;
            }

            AdvanceAndExpect(TokenType.R_PAREN, $"Expected a closing ')' after '{opToken.ToDisplayString()}' arguments.");

            return result;
        }

        /// <summary>
        /// Generates the bytecode for a collective comparison expression.
        /// </summary>
        /// <param name="lhsExprs">The list of left-hand side expressions to compare.</param>
        /// <param name="opToken">The collective comparison operator token.</param>
        /// <param name="rhs">The single right-hand side expression to compare against.</param>
        /// <returns>A TempValue that will hold the final boolean result at runtime.</returns>
        private TempValue GenerateCollectiveComparisonByteCode(List<Value> lhsExprs, Token opToken, Value rhs)
        {
            Value resolvedRhs = ResolveValue(rhs);

            InstructionCode comparisonOp = GetInstructionCodeForCollectiveOp(opToken.Type);
            InstructionCode logicalOp = IsOrCollectiveOperator(opToken.Type) ? InstructionCode.Or : InstructionCode.And;

            TempValue finalResult = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(
                comparisonOp,
                finalResult,
                ResolveValue(lhsExprs[0]),
                resolvedRhs
            ));

            for (int i = 1; i < lhsExprs.Count; i++)
            {
                TempValue nextComparisonResult = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    comparisonOp,
                    nextComparisonResult,
                    ResolveValue(lhsExprs[i]),
                    resolvedRhs
                ));

                // Combine this result with the running total.
                TempValue combinedResult = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    logicalOp,
                    combinedResult,
                    finalResult,
                    nextComparisonResult
                ));

                finalResult = combinedResult;
            }

            return finalResult;
        }

        /// <summary>
        /// Parses range expressions.
        /// This has higher precedence than comparison but lower than addition.
        /// </summary>
        private Value ParseRange()
        {
            Value left = ParseAdditionSubtraction();

            if (_lexer.TokenTypeMatches(TokenType.DOT_DOT))
            {
                _lexer.Advance(); // Consume the '..'.
                // The end of the range.
                Value right = ParseAdditionSubtraction();

                RangeValue range = new RangeValue(ResolveValue(left), ResolveValue(right));
                return range;
            }

            return left;
        }

        /// <summary>
        /// Parses Addition & Subtraction expressions (+, -).
        /// </summary>
        private Value ParseAdditionSubtraction()
        {
            Value left = ParseMulDivModulo();

            while (_lexer.TokenTypeMatches(TokenType.PLUS) || _lexer.TokenTypeMatches(TokenType.MINUS))
            {
                Token op = _lexer.ConsumeToken();
                Value right = ParseMulDivModulo();

                Value result = new TempValue(_currentParseState.NextTempNumber++);
                InstructionCode opcode = (op.Type == TokenType.PLUS)
                    ? InstructionCode.Add
                    : InstructionCode.Subtract;

                _currentParseState.AddCodeInstruction(new InstructionLine(opcode, result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses Multiplication, Division, and Modulo expressions (*, /, %).
        /// </summary>
        private Value ParseMulDivModulo()
        {
            Value left = ParseExponentiation();

            while (IsMultiplicativeOperator(_lexer.PeekNextTokenType()))
            {
                Token op = _lexer.ConsumeToken();
                Value right = ParseExponentiation();

                Value result = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(GetInstructionCodeForBinaryOperator(op.Type), result, ResolveValue(left), ResolveValue(right)));

                left = result;
            }

            return left;
        }

        /// <summary>
        /// Parses exponentiation expressions (**), which are right-associative.
        /// This is the highest precedence binary operator.
        /// </summary>
        private Value ParseExponentiation()
        {
            Value left = ParseUnary();

            while (_lexer.TokenTypeMatches(TokenType.EXPONENT))
            {
                _lexer.Advance();
                Value right = ParseExponentiation();

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.Power,
                    result,
                    ResolveValue(left),
                    ResolveValue(right)
                ));
                return result;
            }

            return left;
        }

        /// <summary>
        /// Parses prefix unary operators (!, -, ~). These are right-associative.
        /// </summary>
        private Value ParseUnary()
        {
            TokenType type = _lexer.PeekNextTokenType();

            if (type == TokenType.COROUTINE)
            {
                return ParseCoroutineExpression();
            }

            if (type is TokenType.BANG or TokenType.MINUS or TokenType.TILDE)
            {
                Token op = _lexer.ConsumeToken();

                Value operand = ResolveValue(ParseUnary());

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);

                InstructionCode opcode = op.Type switch
                {
                    TokenType.BANG => InstructionCode.Not,
                    TokenType.MINUS => InstructionCode.Negate,
                    TokenType.TILDE => InstructionCode.BitwiseNot,
                    _ => InstructionCode.NotImplemented
                };

                _currentParseState.AddCodeInstruction(new InstructionLine(opcode, result, operand));

                return result;
            }

            return ParsePostFix();
        }

        /// <summary>
        /// Parses postfix operators (++, --, !!) and multi-increment/decrement expressions (.++, .--).
        /// Chained postfix operators are not valid in Fluence, such as !!!!, ++++, ----, or ++--, --++ and so on.
        /// </summary>
        private Value ParsePostFix()
        {
            TokenType type = _lexer.PeekNextTokenType();
            if (type is TokenType.DOT_DECREMENT or TokenType.DOT_INCREMENT)
            {
                ParseMultiIncrementDecrementOperators();
                // This operation does not return a value.
                return StatementCompleteValue.StatementCompleted;
            }

            Value left = ParseAccess();

            type = _lexer.PeekNextTokenType();
            if (type is TokenType.INCREMENT or TokenType.DECREMENT or TokenType.BOOLEAN_FLIP)
            {
                Token op = _lexer.ConsumeToken();
                Value originalValue = ResolveValue(left);

                Value modifiedValue;

                if (op.Type == TokenType.BOOLEAN_FLIP)
                {
                    TempValue flippedValue = new TempValue(_currentParseState.NextTempNumber++);
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Not, flippedValue, originalValue));
                    modifiedValue = flippedValue;
                }
                else // ++ and --.
                {
                    InstructionCode operation = (op.Type == TokenType.INCREMENT) ? InstructionCode.Add : InstructionCode.Subtract;

                    _currentParseState.AddCodeInstruction(new InstructionLine(operation, originalValue, originalValue, NumberValue.One));
                    modifiedValue = originalValue;
                }

                if (modifiedValue is not VariableValue)
                {
                    GenerateWriteBackInstruction(left, modifiedValue);
                    return modifiedValue;
                }

                return StatementCompleteValue.StatementCompleted;
            }

            return left;
        }

        /// <summary>
        /// Parses a multi-target increment or decrement operation.
        /// </summary>
        private void ParseMultiIncrementDecrementOperators()
        {
            Token opToken = _lexer.ConsumeToken(); // Consume '.++' or '.--'.

            AdvanceAndExpect(TokenType.L_PAREN, $"Expected an opening '(' after the '{opToken.ToDisplayString()}' operator.");

            InstructionCode operation = (opToken.Type == TokenType.DOT_DECREMENT)
                ? InstructionCode.Subtract
                : InstructionCode.Add;

            do
            {
                Value targetDescriptor = ParseExpression();
                Value currentValue = ResolveValue(targetDescriptor);

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(operation, result, currentValue, NumberValue.One));

                GenerateWriteBackInstruction(targetDescriptor, result);
            }
            while (AdvanceTokenIfMatch(TokenType.COMMA));

            AdvanceAndExpect(TokenType.R_PAREN, $"a closing ')' after the '{opToken.ToDisplayString()}' operator's arguments.");
        }

        /// <summary>
        /// A helper method that generates the correct instruction (Assign, SetField, or SetElement)
        /// to write a value back to the location described by a descriptor.
        /// </summary>
        /// <param name="descriptor">The original Value, which may be a simple VariableValue or a complex descriptor like PropertyAccessValue.</param>
        /// <param name="valueToAssign">The Value (usually a TempValue) that holds the result to be written.</param>
        private void GenerateWriteBackInstruction(Value descriptor, Value valueToAssign)
        {
            switch (descriptor)
            {
                case VariableValue variable:
                    if (valueToAssign is LambdaValue lambda)
                    {
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewLambda, GetOrCreateVariable(Mangler.Mangle(variable.Name, lambda.Function.Arity)), valueToAssign));
                        return;
                    }

                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, variable, valueToAssign));
                    break;
                case PropertyAccessValue propAccess:
                    Value targetObject = ResolveValue(propAccess.Target);
                    _currentParseState.AddCodeInstruction(new InstructionLine(
                        InstructionCode.SetField,
                        targetObject,
                        new StringValue(propAccess.FieldName),
                        valueToAssign
                    ));
                    break;
                case ElementAccessValue elementAccess:
                    Value targetCollection = ResolveValue(elementAccess.Target);
                    Value index = ResolveValue(elementAccess.Index);
                    _currentParseState.AddCodeInstruction(new InstructionLine(
                        InstructionCode.SetElement,
                        targetCollection,
                        index,
                        valueToAssign
                    ));
                    break;
                case StaticStructAccess statAccess:
                    if (statAccess.Struct.StaticFields.ContainsKey(statAccess.Name))
                    {
                        throw ConstructParserException($"Attempted to modify a solid ( static ) struct field: {statAccess.Struct}__Field:{statAccess.Name}.", _lexer.PeekNextToken());
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Ensures that a given Value is a simple, usable value
        /// rather than an abstract descriptor. If the input Value is a descriptor (like PropertyAccessValue
        /// or ElementAccessValue), this method generates the necessary GetField or GetElement bytecode
        /// to retrieve the actual value and returns the TempValue that will hold the result at runtime.
        /// </summary>
        /// <param name="val">The Value to resolve.</param>
        /// <returns>A simple Value that can be used as an operand in other instructions.</returns>
        private Value ResolveValue(Value val)
        {
            if (val is not (PropertyAccessValue or ElementAccessValue or StaticStructAccess))
            {
                return val;
            }

            if (val is StaticStructAccess statAccess)
            {
                TempValue result = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.GetStatic,
                    result,
                    statAccess.Struct,
                    new StringValue(statAccess.Name)
                ));

                return result;
            }

            if (val is PropertyAccessValue propAccess)
            {
                Value resolvedTarget = ResolveValue(propAccess.Target);

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.GetField,
                    result,
                    resolvedTarget,
                    new StringValue(propAccess.FieldName)
                ));

                return result;
            }

            if (val is ElementAccessValue elementAccess)
            {
                Value resolvedCollection = ResolveValue(elementAccess.Target);
                Value resolvedIndex = ResolveValue(elementAccess.Index);

                TempValue result = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.GetElement,
                    result,
                    resolvedCollection,
                    resolvedIndex
                ));

                return result;
            }

            // This should be unreachable, but it satisfies the compiler.
            return val;
        }

        /// <summary>
        /// Parses a constructor call via parentheses.
        /// </summary>
        /// <param name="structSymbol">The symbol for the struct being instantiated.</param>
        /// <returns>A TempValue that will hold the new struct instance at runtime.</returns>
        private TempValue ParseConstructorCall(StructSymbol structSymbol)
        {
            AdvanceAndExpect(TokenType.L_PAREN, $"Expected an opening '(' for the constructor call to '{structSymbol.Name}'.");

            TempValue instance = CreateNewInstance(structSymbol);

            List<Value> arguments = ParseArgumentList();

            AdvanceAndExpect(TokenType.R_PAREN, $"Expected closing ')' for the constructor call to '{structSymbol.Name}'.");

            // Check if an `init` method should be called.
            if (structSymbol.Constructors.Count != 0)
            {
                foreach (Value arg in arguments)
                {
                    _currentParseState.AddCodeInstruction(new InstructionLine(arg is ReferenceValue ? InstructionCode.LoadAddress : InstructionCode.PushParam, ResolveValue(arg)));
                }

                TempValue ignoredResult = new TempValue(_currentParseState.NextTempNumber++);

                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.CallMethod,
                    ignoredResult,
                    instance,
                    new StringValue(Mangler.Mangle("init", arguments.Count)
                )));
            }
            else if (arguments.Count > 0)
            {
                // No user-defined constructor, but arguments were provided. This is an error.
                Token errorToken = _lexer.PeekCurrentToken();
                throw ConstructParserException(
                    $"Invalid constructor call for '{structSymbol.Name}'. Struct '{structSymbol.Name}' has no 'init' constructor and cannot be called with arguments.",
                    errorToken
                );
            }

            _lhsPool.Return(arguments);

            return instance;
        }

        /// <summary>
        /// Parses a direct struct initializer using brace syntax.
        /// </summary>
        /// <param name="structSymbol">The symbol for the struct being instantiated.</param>
        /// <returns>A TempValue that will hold the new struct instance at runtime.</returns>
        private TempValue ParseDirectInitializer(StructSymbol structSymbol)
        {
            AdvanceAndExpect(TokenType.L_BRACE, $"an opening '{{' for the direct initializer of '{structSymbol.Name}'.");

            TempValue instance = CreateNewInstance(structSymbol);
            HashSet<string> initializedFields = new HashSet<string>();

            if (!_lexer.TokenTypeMatches(TokenType.R_BRACE))
            {
                do
                {
                    Token fieldToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a field name in the struct initializer.");
                    string fieldName = fieldToken.Text;

                    if (!structSymbol.Fields.Contains(fieldName))
                    {
                        throw ConstructParserException($"Invalid field '{fieldName}'. Struct '{structSymbol.Name}' does not have a field with this name.", fieldToken);
                    }
                    if (!initializedFields.Add(fieldName))
                    {
                        throw ConstructParserException($"Duplicate field '{fieldName}'. Each field can only be initialized once.", fieldToken);
                    }

                    AdvanceAndExpect(TokenType.COLON, $"Expected a ':' after the field name '{fieldName}'.");
                    Value fieldValue = ParseExpression();

                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.SetField, instance, new StringValue(fieldName), ResolveValue(fieldValue)));

                } while (AdvanceTokenIfMatch(TokenType.COMMA));
            }

            AdvanceAndExpect(TokenType.R_BRACE, "Expected a closing '}' to end the struct initializer.");

            return instance;
        }

        /// <summary>
        /// A helper method that generates the NewInstance bytecode instruction for a given struct.
        /// </summary>
        private TempValue CreateNewInstance(StructSymbol symbol)
        {
            TempValue instance = new TempValue(_currentParseState.NextTempNumber++);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.NewInstance, instance, symbol));
            return instance;
        }

        /// <summary>
        /// Parses postfix expressions, which include function calls `()`, index access `[]`, and property access `.`.
        /// This method is called repeatedly in a loop to handle chained accesses.
        /// </summary>
        /// <returns>A Value representing the result of the access chain.</returns>
        private Value ParseAccess(bool allowCalls = true)
        {
            Value left = ParsePrimary();

            while (true)
            {
                TokenType type = _lexer.PeekNextTokenType();

                // Access get/set.
                if (type == TokenType.L_BRACKET)
                {
                    left = ParseIndexAccess(left);
                }
                // Null conditional '?.' operator.
                else if (type == TokenType.NULL_COND)
                {
                    TempValue isNullTemp = new TempValue(_currentParseState.NextTempNumber++);
                    _currentParseState.CodeInstructions.Add(new InstructionLine(InstructionCode.Equal, isNullTemp, left, NilValue.NilInstance));

                    int ifIndex = _currentParseState.CodeInstructions.Count;
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, null!, isNullTemp));

                    _lexer.Advance();

                    Token memberToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a member name after '.' .");

                    switch (left)
                    {
                        case VariableValue variable:
                            if (_currentParseState.CurrentScope.TryResolve(variable.Hash, out Symbol symbol) && symbol is EnumSymbol enumSymbol)
                            {
                                if (enumSymbol.Members.TryGetValue(memberToken.Text, out EnumValue enumValue))
                                {
                                    left = enumValue;
                                }
                                else
                                {
                                    throw ConstructParserException($"Enum '{enumSymbol.Name}' does not have a member named '{memberToken.Text}'.", memberToken);
                                }
                            }
                            else
                            {
                                left = new PropertyAccessValue(left, memberToken.Text);
                            }
                            break;
                        case StaticStructAccess staticAccess:
                            left = new StaticStructAccess(staticAccess.Struct, memberToken.Text);
                            break;
                        default:
                            left = new PropertyAccessValue(left, memberToken.Text);
                            break;
                    }

                    _currentParseState.CodeInstructions[ifIndex].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count + 1);
                }
                // Property access.
                else if (type == TokenType.DOT)
                {
                    _lexer.Advance(); // Consume the dot.

                    Token memberToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected a member name after '.' .");

                    switch (left)
                    {
                        case VariableValue variable:
                            if (_currentParseState.CurrentScope.TryResolve(variable.Hash, out Symbol symbol) && symbol is EnumSymbol enumSymbol)
                            {
                                if (enumSymbol.Members.TryGetValue(memberToken.Text, out EnumValue enumValue))
                                {
                                    left = enumValue;
                                }
                                else
                                {
                                    throw ConstructParserException($"Enum '{enumSymbol.Name}' does not have a member named '{memberToken.Text}'.", memberToken);
                                }
                            }
                            else
                            {
                                left = new PropertyAccessValue(left, memberToken.Text);
                            }
                            break;
                        case StaticStructAccess staticAccess:
                            left = new StaticStructAccess(staticAccess.Struct, memberToken.Text);
                            break;
                        default:
                            left = new PropertyAccessValue(left, memberToken.Text);
                            break;
                    }
                }
                else if (type == TokenType.L_PAREN && allowCalls)
                {
                    left = ParseFunctionCall(left);
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        /// <summary>
        /// Parses a function or method call, assuming the callable expression (`left`) has already been parsed.
        /// </summary>
        private TempValue ParseFunctionCall(Value callable)
        {
            _lexer.Advance(); // Consume '('.
            List<Value> arguments = ParseArgumentList();

            AdvanceAndExpect(TokenType.R_PAREN, "Expected a closing ')' for function call after function arguments.");

            foreach (Value arg in arguments)
            {
                _currentParseState.AddCodeInstruction(new InstructionLine(arg is ReferenceValue ? InstructionCode.LoadAddress : InstructionCode.PushParam, ResolveValue(arg)));
            }

            TempValue result = new TempValue(_currentParseState.NextTempNumber++);
            string templated;

            if (callable is PropertyAccessValue propAccess)
            {
                templated = Mangler.Mangle(propAccess.FieldName, arguments.Count);

                _currentParseState.AddCodeInstruction(new InstructionLine(
                   InstructionCode.CallMethod,
                   result,
                   ResolveValue(propAccess.Target),
                   new StringValue(templated)
               ));
            }
            else if (callable is StaticStructAccess statAccess)
            {
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.CallStatic,
                    result,
                    statAccess.Struct,
                    new StringValue(Mangler.Mangle(statAccess.Name, arguments.Count))
                ));
            }
            else if (callable is LambdaValue lambda)
            {
                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.CallFunction,
                    result,
                    lambda.Function,
                    new NumberValue(arguments.Count)
                ));
            }
            else if (callable is VariableValue var)
            {
                templated = Mangler.Mangle(var.Name, arguments.Count);

                _currentParseState.AddCodeInstruction(new InstructionLine(
                    InstructionCode.CallFunction,
                    result,
                    GetOrCreateVariable(templated),
                    new NumberValue(arguments.Count)
                ));
            }

            _lhsPool.Return(arguments);

            return result;
        }

        private TempValue ParseIsAStatement(VariableValue variable)
        {
            _lexer.Advance(); // Consume the variable.

            Token type = _lexer.ConsumeToken();

            if (type.Type is not TokenType.IDENTIFIER)
            {
                throw ConstructParserExceptionWithUnexpectedToken("Expected an identifier of a trait or a struct type name after 'is' keyword", type);
            }
            else
            {
                int hash = type.Text.GetHashCode();

                if (!_currentParseState.CurrentScope.TryResolve(hash, out _) && !_currentParseState.GlobalScope.TryResolve(hash, out _))
                {
                    throw ConstructParserException($"Unknown type: {type.Text} in 'is' expression", type);
                }

                TempValue condition = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.IsType, condition, variable, new StringValue(type.Text)));

                return condition;
            }
        }

        /// <summary>
        /// Parses an 'N times' statement that accepts either a raw integer number, or a variable that is a number.
        /// </summary>
        /// <param name="count">The amount of times to repeat the statements or the expression.</param>
        private void ParseTimesStatement(Value count)
        {
            _lexer.Advance(); // Consume 'times'.

            Value condition;

            if (_lexer.PeekNextTokenType() == TokenType.AS)
            {
                _lexer.Advance();
                bool isReadonly = false;

                if (_lexer.PeekNextTokenType() == TokenType.SOLID)
                {
                    _lexer.Advance();
                    isReadonly = true;
                }

                Token nameToken = ConsumeAndExpect(TokenType.IDENTIFIER, "Expected an identifier for a 'x times as y' statement");
                condition = GetOrCreateVariable(nameToken.Text, isReadonly);
                GenerateWriteBackInstruction(condition, NumberValue.Zero);
            }
            else
            {
                condition = new TempValue(_currentParseState.NextTempNumber++);
                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, condition, NumberValue.Zero));
            }

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            int jumpToCheckIndex = _currentParseState.CodeInstructions.Count - 1;

            int bodyStartIndex = _currentParseState.CodeInstructions.Count;

            LoopOrMatchContext loopContext = new LoopOrMatchContext();
            _currentParseState.ActiveLoopContexts.Push(loopContext);

            ParseStatementBody("Expected an '->' for a single-line while loop body.");

            _lexer.InsertNextToken(TokenType.EOL);
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.IncrementIntUnrestricted, condition));

            int checkStartIndex = _currentParseState.CodeInstructions.Count;
            _currentParseState.CodeInstructions[jumpToCheckIndex].Lhs = new GoToValue(checkStartIndex);

            TempValue truthy = new TempValue(_currentParseState.NextTempNumber++);

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.LessThan, truthy, condition, count));
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GotoIfTrue, new GoToValue(bodyStartIndex), truthy));

            int loopEndIndex = _currentParseState.CodeInstructions.Count;
            int continueAddress = checkStartIndex - 1;

            PatchLoopExits(loopContext, loopEndIndex, continueAddress);

            _currentParseState.ActiveLoopContexts.Pop();
        }

        /// <summary>
        /// Parses the body of a lambda expression.
        /// </summary>
        /// <returns>The parsed lambda value.</returns>
        private LambdaValue ParseLambda()
        {
            // '(' is already consumed.

            int startAddressInSource = _lexer.PeekCurrentToken().LineInSourceCode;

            List<string> args = new List<string>();
            int refMask = 0;
            int argIndex = 0;
            bool argByRef = false;

            while (true)
            {
                TokenType type = _lexer.PeekNextTokenType();
                if (type == TokenType.COMMA)
                {
                    _lexer.Advance();
                }
                else if (type == TokenType.REF)
                {
                    argByRef = true;

                    if (argIndex >= 32) throw ConstructParserException("Argument limit (32) exceeded ( What are you even doing? ).", _lexer.PeekCurrentToken());

                    refMask |= 1 << argIndex;

                    _lexer.Advance();

                    if (_lexer.PeekNextTokenType() != TokenType.IDENTIFIER)
                    {
                        throw ConstructParserExceptionWithUnexpectedToken("Expected an argument identifier after a 'ref' keyword", _lexer.PeekCurrentToken());
                    }
                }
                else if (type == TokenType.IDENTIFIER)
                {
                    Token arg = _lexer.ConsumeToken();
                    args.Add(arg.Text);

                    if (argByRef)
                    {
                        argByRef = false;
                    }
                    argIndex++;
                }
                else if (type == TokenType.R_PAREN)
                {
                    break;
                }
                else
                {
                    throw ConstructParserExceptionWithUnexpectedToken($"Unidentified token inside lambda argument list: {_lexer.PeekNextToken()}, expected only identifiers", _lexer.PeekNextToken());
                }
            }

            AdvanceAndExpect(TokenType.R_PAREN, "Expected a closing ')' for lambda declaration");
            AdvanceAndExpect(TokenType.ARROW, "Expected an '=>' for the beginning of the lambda body");

            int lambdaBodySkipIndex = _currentParseState.CodeInstructions.Count;
            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Goto, null!));
            _currentParseState.AddCodeInstruction(LambdaEntrance);

            int lambdaCodeStartIndex = _currentParseState.CodeInstructions.Count;

            if (_lexer.PeekNextTokenType() == TokenType.L_BRACE)
            {
                ParseBlockStatement();
                _lexer.InsertNextToken(TokenType.EOL);
                // If block doesnt end with return the VM will explode.
                if (_currentParseState.CodeInstructions[^1].Instruction != InstructionCode.Return)
                {
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Return, NilValue.NilInstance));
                }
            }
            else
            {
                Value ret = ResolveValue(ParseTernary());
                _currentParseState.CodeInstructions.Add(new InstructionLine(InstructionCode.Return, ret));
            }

            _currentParseState.AddCodeInstruction(LambdaClosure);

            _currentParseState.CodeInstructions[lambdaBodySkipIndex].Lhs = new GoToValue(_currentParseState.CodeInstructions.Count);
            FunctionValue lambdaFunction = new FunctionValue($"lambda__{args.Count}", false, args.Count, lambdaCodeStartIndex, startAddressInSource, args, refMask, _currentParseState.CurrentScope);

            if (_vmConfiguration.OptimizeByteCode)
            {
                FluenceOptimizer.OptimizeChunk(
                    _currentParseState.CodeInstructions,
                    _currentParseState,
                    lambdaCodeStartIndex,
                    _vmConfiguration
                );
            }

            int functionCodeEnd = _currentParseState.CodeInstructions.Count;

            int nextSlotIndex = 0;

            Dictionary<int, int> variableSlotMap = new Dictionary<int, int>();
            Dictionary<int, int> tempSlotMap = new Dictionary<int, int>();

            for (int i = 0; i < lambdaFunction.Arguments.Count; i++)
            {
                variableSlotMap[lambdaFunction.Arguments[i].GetHashCode()] = nextSlotIndex++;
            }

            for (int i = lambdaFunction.StartAddress; i < functionCodeEnd; i++)
            {
                InstructionLine insn = _currentParseState.CodeInstructions[i];
                if (insn.Instruction == InstructionCode.SectionLambdaStart)
                {
                    SkipNestedLambdaBlock(_currentParseState.CodeInstructions, ref i, functionCodeEnd);
                    continue;
                }

                OperandUsage usage = _operandUsageMap[(int)insn.Instruction];

                if (usage.HasFlag(OperandUsage.Lhs))
                    ProcessValue(insn.Lhs, variableSlotMap, tempSlotMap, ref nextSlotIndex);
                if (usage.HasFlag(OperandUsage.Rhs))
                    ProcessValue(insn.Rhs, variableSlotMap, tempSlotMap, ref nextSlotIndex);
                if (usage.HasFlag(OperandUsage.Rhs2))
                    ProcessValue(insn.Rhs2, variableSlotMap, tempSlotMap, ref nextSlotIndex);
                if (usage.HasFlag(OperandUsage.Rhs3))
                    ProcessValue(insn.Rhs3, variableSlotMap, tempSlotMap, ref nextSlotIndex);
            }

            lambdaFunction.TotalRegisterSlots = nextSlotIndex;

            for (int i = 0; i < _currentParseState.FunctionVariableDeclarations.Count; i++)
            {
                InstructionLine line = _currentParseState.FunctionVariableDeclarations[i];
                if (line.Rhs is FunctionValue fun && fun.Hash == lambdaFunction.Hash)
                {
                    _currentParseState.FunctionVariableDeclarations[i].Rhs = lambdaFunction;
                    break;
                }
            }

            return new LambdaValue(lambdaFunction);
        }

        /// <summary>
        /// Parses a comma-separated list of arguments until a closing parenthesis is encountered.
        /// </summary>
        /// <returns>A list of Values representing the parsed arguments.</returns>
        private List<Value> ParseArgumentList()
        {
            List<Value> arguments = _lhsPool.Get();
            if (!_lexer.TokenTypeMatches(TokenType.R_PAREN))
            {
                do
                {
                    arguments.Add(ParseTernary());
                } while (AdvanceTokenIfMatch(TokenType.COMMA));
            }
            return arguments;
        }

        /// <summary>
        /// Parses a custom <see cref="TokenType"/>-separated list of one or more expressions.
        /// </summary>
        /// <returns>A list of Value objects representing the parsed expressions.</returns>
        private List<Value> ParseTokenSeparatedArguments(TokenType token)
        {
            List<Value> arguments = _lhsPool.Get();
            do
            {
                arguments.Add(ParseTernary());
            } while (AdvanceTokenIfMatch(token));

            return arguments;
        }

        /// <summary>
        /// Parses an index access expression assuming the collection has been parsed.
        /// </summary>
        private ElementAccessValue ParseIndexAccess(Value left)
        {
            _lexer.Advance(); // Consume '['.

            Value index = ParseExpression();

            AdvanceAndExpect(TokenType.R_BRACKET, "Expected a closing ']' for the index accessor.");

            // Creates a descriptor for the access. This will be resolved into a GetElement
            // or SetElement instruction by a higher-level parsing method.
            return new ElementAccessValue(left, index);
        }

        /// <summary>
        /// Scans forward in the bytecode to find the matching SectionLambdaEnd for a nested block,
        /// correctly handling nested lambdas.
        /// </summary>
        /// <param name="bytecode">The list of bytecode instructions.</param>
        /// <param name="currentIndex">The current instruction index, passed by reference. It will be updated to the index of the matching SectionLambdaEnd.</param>
        /// <param name="endOffset">The exclusive end index for the scan.</param>
        private static void SkipNestedLambdaBlock(List<InstructionLine> bytecode, ref int currentIndex, int endOffset)
        {
            int nestingLevel = 1;
            currentIndex++;

            while (nestingLevel > 0 && currentIndex < endOffset)
            {
                InstructionLine insn = bytecode[currentIndex];

                if (insn.Instruction == InstructionCode.SectionLambdaStart)
                {
                    nestingLevel++;
                }
                else if (insn.Instruction == InstructionCode.SectionLambdaEnd)
                {
                    nestingLevel--;
                }

                if (nestingLevel > 0)
                {
                    currentIndex++;
                }
            }
        }


        /// <summary>
        /// Parses a coroutine expression.
        /// </summary>
        private TempValue ParseCoroutineExpression()
        {
            _lexer.Advance(); // Consume 'coroutine'.

            string functionName = _lexer.ConsumeToken().Text;

            AdvanceAndExpect(TokenType.L_PAREN, "Expected '(' after function identifier in coroutine statement.");

            int argCount = 0;

            if (!_lexer.TokenTypeMatches(TokenType.R_PAREN))
            {
                do
                {
                    Value argValue = ResolveValue(ParseTernary());
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.PushParam, argValue));
                    argCount++;
                }
                while (AdvanceTokenIfMatch(TokenType.COMMA));
            }

            AdvanceAndExpect(TokenType.R_PAREN, "Expected ')' after coroutine arguments.");

            TempValue coroRegister = new TempValue(_currentParseState.NextTempNumber++);

            _currentParseState.AddCodeInstruction(new InstructionLine(
                InstructionCode.NewCoroutine,
                coroRegister,
                new VariableValue($"{functionName}__{argCount}", false),
                new NumberValue(argCount, NumberValue.NumberType.Integer)
            ));

            return coroRegister;
        }

        /// <summary>
        /// Parses a yield coroutine expression.
        /// </summary>
        private TempValue ParseYieldExpression()
        {
            Value valueToYield = NilValue.NilInstance;
            TokenType next = _lexer.PeekNextTokenType();

            if (next is not TokenType.EOL and not TokenType.EOF and not TokenType.R_PAREN and not TokenType.R_BRACE and not TokenType.COMMA)
            {
                valueToYield = ResolveValue(ParseTernary());
            }

            TempValue resultRegister = new TempValue(_currentParseState.NextTempNumber++);

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Yield, resultRegister, valueToYield));

            return resultRegister;
        }

        /// <summary>
        /// Parses a resume coroutine expression.
        /// </summary>
        private TempValue ParseResumeExpression()
        {
            Value coroutineToResume = ResolveValue(ParseTernary());

            Value argumentToPassIn = NilValue.NilInstance;

            if (_lexer.PeekNextTokenType() == TokenType.COMMA)
            {
                _lexer.Advance();
                argumentToPassIn = ResolveValue(ParseTernary());
            }

            TempValue resultRegister = new TempValue(_currentParseState.NextTempNumber++);

            _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Resume, resultRegister, coroutineToResume, argumentToPassIn));

            return resultRegister;
        }

        /// <summary>
        /// Parses a primary expression, which is the highest level of precedence.
        /// This includes literals (numbers, strings, etc.), identifiers, grouping parentheses,
        /// and prefix unary operators.
        /// </summary>
        /// <returns>A Value representing the parsed primary expression.</returns>
        private Value ParsePrimary()
        {
            if (AdvanceTokenIfMatch(TokenType.TYPE_OF))
            {
                Value operand = ParseAccess();

                TempValue resultRegister = new TempValue(_currentParseState.NextTempNumber++);

                Value typeOperand;
                if (operand is VariableValue varValue)
                {
                    bool found = _currentParseState.CurrentScope.TryResolve(varValue.Hash, out Symbol sb);
                    if (found && sb is VariableSymbol)
                    {
                        typeOperand = varValue;
                    }
                    else if (_currentParseState.CurrentScope.TryResolve(varValue.Hash, out _))
                    {
                        // It's a raw type name.
                        typeOperand = new StringValue(varValue.Name);
                    }
                    else
                    {
                        typeOperand = operand;
                    }
                }
                else
                {
                    typeOperand = operand;
                }

                _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.GetType, resultRegister, typeOperand));

                return resultRegister;
            }

            Token token = _lexer.ConsumeToken();

            if (token.Type == TokenType.NIL)
            {
                return NilValue.NilInstance;
            }

            switch (token.Type)
            {
                case TokenType.IDENTIFIER:
                    string name = token.Text;
                    if (_currentParseState.CurrentScope.TryResolve(name.GetHashCode(), out Symbol symbol) && symbol is StructSymbol structSymbol)
                    {
                        // It's a struct, check if it's a constructor call Vec2(2,3).
                        // or a direct initializer Vec2{ x: 2, y: 3 }.
                        if (_lexer.TokenTypeMatches(TokenType.L_PAREN))
                        {
                            return ParseConstructorCall(structSymbol);
                        }
                        else if (_lexer.TokenTypeMatches(TokenType.L_BRACE))
                        {
                            return ParseDirectInitializer(structSymbol);
                        }
                        // Static struct field or method.
                        else if (_lexer.TokenTypeMatches(TokenType.DOT))
                        {
                            return new StaticStructAccess(structSymbol, null!);
                        }
                        else
                        {
                            // This is most likely the raw name of a type, most likely for the 'typeof' operator.
                            return new StringValue(name);
                        }
                    }
                    else if (symbol is VariableSymbol varSymbol)
                    {
                        TempValue temp = new TempValue(_currentParseState.NextTempNumber++);
                        _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Assign, temp, varSymbol.Value));

                        return temp;
                    }
                    else
                    {
                        // "x times" consumes the identifier, and dispatches the appropriate method.
                        // But must return a value, so we return StatementComplete.
                        if (_lexer.PeekNextTokenType() == TokenType.TIMES)
                        {
                            ParseTimesStatement(GetOrCreateVariable(name));
                            return StatementCompleteValue.StatementCompleted;
                        }
                        else if (_lexer.PeekNextTokenType() == TokenType.IS)
                        {
                            // Some form of pattern matching, currently only a check whether a struct implements a trait, TO DO pattern matching for other cases.
                            return ParseIsAStatement(GetOrCreateVariable(name));
                        }

                        // Otherwise, it's just a regular variable.
                        return GetOrCreateVariable(name);
                    }
                case TokenType.NUMBER:
                    // "x times" consumes the number, and dispatches the appropriate method.
                    // But must return a value, so we return StatementComplete.
                    if (_lexer.PeekNextTokenType() == TokenType.TIMES)
                    {
                        ParseTimesStatement(NumberValue.FromToken(token));
                        return StatementCompleteValue.StatementCompleted;
                    }
                    return NumberValue.FromToken(token);
                case TokenType.STRING: return new StringValue(token.Literal.ToString()!);
                case TokenType.TRUE: return BooleanValue.True;
                case TokenType.FALSE: return BooleanValue.False;
                case TokenType.F_STRING: return ParseFString(token.Literal);
                case TokenType.CHARACTER: return new CharValue((char)token.Literal);
                case TokenType.L_BRACKET: return ParseList();
                case TokenType.MATCH: return ParseMatchStatement();
                case TokenType.L_BRACE: return ParseDictionary();
                case TokenType.YIELD: return ParseYieldExpression();
                case TokenType.RESUME: return ParseResumeExpression();
                case TokenType.THROW:
                    Value val = ResolveValue(ParseExpression());
                    _currentParseState.AddCodeInstruction(new InstructionLine(InstructionCode.Throw, val));
                    return StatementCompleteValue.StatementCompleted;
                case TokenType.REF:
                    Value toRef = ParseExpression();

                    if (toRef as VariableValue is not null)
                    {
                        return new ReferenceValue((VariableValue)toRef);
                    }

                    throw ConstructParserException("Can not pass an argument to a function by reference if the argument is not a variable.", _lexer.PeekNextToken());
                case TokenType.SELF:
                    if (_currentParseState.CurrentStructContext == null)
                    {
                        throw ConstructParserException("The 'self' keyword can only be used inside a struct method.", token);
                    }
                    // The 'self' keyword is just a special, pre-defined local variable.
                    // At runtime, the VM will ensure the instance is available.
                    return VariableValue.SelfVariable;
                case TokenType.L_PAREN:
                    if (IsALambda())
                    {
                        return ParseLambda();
                    }
                    Value expr = ParseTernary();
                    AdvanceAndExpect(TokenType.R_PAREN, "Expected: a closing ')' to match the opening parenthesis.");
                    return expr;
            }

            // If we've fallen through the entire switch, we have an invalid token.
            throw ConstructParserExceptionWithUnexpectedToken($"Expected an expression, a literal (number, string, etc.), a variable, or '('.", token);
        }

        /// <summary>
        /// Parses the body of a statement, whether a block body or a single line expression.
        /// </summary>
        /// <param name="errorMsgForSingleLine">The error to display when there is no '->' in a single line expression body.</param>
        private void ParseStatementBody(string errorMsgForSingleLine)
        {
            if (_lexer.TokenTypeMatches(TokenType.L_BRACE))
            {
                ParseBlockStatement();
            }
            else
            {
                AdvanceAndExpect(TokenType.THIN_ARROW, errorMsgForSingleLine);
                ParseStatement();
            }
        }

        /// <summary>
        /// Checks if the next token's type matches the expected type.
        /// If it matches, the token is consumed and the method returns true.
        /// If it does not match, the token is not consumed and the method returns false.
        /// </summary>
        private bool AdvanceTokenIfMatch(TokenType expectedType)
        {
            if (_lexer.TokenTypeMatches(expectedType))
            {
                _lexer.Advance();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Helper method to handle patching 'break' and 'continue'.
        /// </summary>
        private void PatchLoopExits(LoopOrMatchContext loopContext, int breakAddress, int continueAddress)
        {
            foreach (int patchIndex in loopContext.BreakPatchAddresses)
            {
                _currentParseState.CodeInstructions[patchIndex].Lhs = new GoToValue(breakAddress);
            }

            foreach (int patchIndex in loopContext.ContinuePatchAddresses)
            {
                _currentParseState.CodeInstructions[patchIndex].Lhs = new GoToValue(continueAddress);
            }
        }

        /// <summary>
        /// Consumes the next token from the lexer and throws a formatted parser exception if it does not match the expected type.
        /// </summary>
        /// <param name="expectedType">The TokenType that is grammatically required at this point in the stream.</param>
        /// <returns>The consumed token if it matches the expected type.</returns>
        /// <exception cref="FluenceParserException">Thrown if the consumed token's type does not match the expectedType.</exception>
        private Token ConsumeAndExpect(TokenType expectedType, string errorMessage)
        {
            Token token = _lexer.ConsumeToken();
            if (token.Type != expectedType)
            {
                throw ConstructParserExceptionWithUnexpectedToken(errorMessage, token);
            }
            return token;
        }

        /// <summary>
        /// Verifies that the next token in the stream is of the expected type and advances the stream.
        /// </summary>
        /// <param name="expectedType">The TokenType that is grammatically required.</param>
        /// <param name="expectedDescription">A user-friendly description of what was expected, for error reporting.</param>
        /// <exception cref="FluenceParserException">Thrown if the next token's type does not match.</exception>
        private void AdvanceAndExpect(TokenType expectedType, string errorMessage)
        {
            if (!_lexer.TokenTypeMatches(expectedType))
            {
                throw ConstructParserExceptionWithUnexpectedToken(errorMessage, _lexer.PeekNextToken());
            }
            _lexer.Advance();
        }

        private FluenceParserException ConstructParserExceptionWithUnexpectedToken(string errorMessage, Token unexpectedToken)
        {
            string source;

            if (_multiFileProject)
            {
                source = File.ReadAllText(_currentParsingFileName);
            }
            else
            {
                source = _lexer.SourceCode;
            }

            ParserExceptionContext context = new ParserExceptionContext()
            {
                FileName = _currentParsingFileName,
                Column = unexpectedToken.ColumnInSourceCode,
                FaultyLine = FluenceDebug.TruncateLine(FluenceLexer.GetCodeLineFromSource(source, unexpectedToken.LineInSourceCode)),
                LineNum = unexpectedToken.LineInSourceCode,
                UnexpectedToken = unexpectedToken,
            };
            return new FluenceParserException(errorMessage, context);
        }

        private FluenceParserException ConstructParserException(string errorMessage, Token infoToken)
        {
            string source;

            if (_multiFileProject)
            {
                source = File.ReadAllText(_currentParsingFileName);
            }
            else
            {
                source = _lexer.SourceCode;
            }

            ParserExceptionContext context = new ParserExceptionContext()
            {
                FileName = _currentParsingFileName,
                Column = infoToken.ColumnInSourceCode,
                FaultyLine = FluenceDebug.TruncateLine(FluenceLexer.GetCodeLineFromSource(source, infoToken.LineInSourceCode), 100),
                LineNum = infoToken.LineInSourceCode,
                UnexpectedToken = Token.NoUse,
            };
            return new FluenceParserException(errorMessage, context);
        }

        /// <summary>
        /// Peeks ahead to see whether an expression that starts with a left parentheses is a lambda declaration.
        /// </summary>
        /// <returns>True if it is.</returns>
        private bool IsALambda()
        {
            int lookAhead = 1;

            while (true)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(lookAhead);

                if (type is not TokenType.COMMA and not TokenType.IDENTIFIER and not TokenType.R_PAREN and not TokenType.REF)
                {
                    return false;
                }

                if (type == TokenType.R_PAREN && _lexer.PeekTokenTypeAheadByN(lookAhead + 1) == TokenType.ARROW)
                {
                    return true;
                }

                lookAhead++;
            }
        }

        /// <summary>
        /// Checks if an assignment operator pipe is of the optional type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns>True if it is an optional assignment pipe operator.</returns>
        private static bool IsOptionalChainAssignmentOperator(TokenType type) =>
            type is TokenType.OPTIONAL_ASSIGN_N or TokenType.OPTIONAL_REST_ASSIGN or TokenType.OPTIONAL_CHAIN_N_UNIQUE_ASSIGN;

        /// <summary>
        /// Checks if a token type is a multiplicative operator (*, /, %).
        /// </summary>
        private static bool IsMultiplicativeOperator(TokenType type) =>
            type is TokenType.STAR or TokenType.SLASH or TokenType.PERCENT;

        /// <summary>
        /// Checks if a token type is a simple assignment operator (=, +=, -=, etc.).
        /// </summary>
        private static bool IsSimpleAssignmentOperator(TokenType type) =>
            type is >= TokenType.EQUAL and <= TokenType.EQUAL_AMPERSAND;

        /// <summary>
        /// Checks if a token type is one of the chain-assignment operators (<|, <n|, <?|, etc.).
        /// </summary>
        private static bool IsChainAssignmentOperator(TokenType type) =>
            type is >= TokenType.CHAIN_ASSIGN_N and <= TokenType.OPTIONAL_SEQUENTIAL_REST_ASSIGN;

        /// <summary>
        /// Checks if a token type is a multi-target compound assignment operator (.+=, .-=, etc.).
        /// </summary>
        private static bool IsMultiCompoundAssignmentOperator(TokenType type) => type switch
        {
            TokenType.DOT_PLUS_EQUAL or
            TokenType.DOT_MINUS_EQUAL or
            TokenType.DOT_STAR_EQUAL or
            TokenType.DOT_SLASH_EQUAL => true,
            _ => false,
        };

        /// <summary>
        /// Checks if a token type is a standard comparison operator (>, <, >=, <=).
        /// </summary>
        private static bool IsStandardComparisonOperator(TokenType type) =>
            type is TokenType.GREATER or
            TokenType.LESS or
            TokenType.GREATER_EQUAL or
            TokenType.LESS_EQUAL;

        /// <summary>
        /// Checks if a token type is a collective comparison operator (<==|, <||==|, etc.).
        /// </summary>
        private static bool IsCollectiveOperator(TokenType type) =>
            type is >= TokenType.COLLECTIVE_EQUAL and <= TokenType.COLLECTIVE_OR_GREATER_EQUAL;

        /// <summary>
        /// Peeks ahead to determine if a match statement is using the switch-style syntax (`case:`)
        /// or the expression-style syntax (`case ->`).
        /// </summary>
        private bool IsSwitchStyleMatch()
        {
            int lookAhead = 1;
            while (true)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(lookAhead);
                if (type is TokenType.THIN_ARROW or TokenType.ARROW) return false;
                if (type == TokenType.COLON) return true;
                if (type == TokenType.R_BRACE) return false; // Empty match.
                lookAhead++;
            }
        }

        /// <summary>
        /// Peeks ahead to see if the upcoming tokens form a collective comparison expression.
        /// </summary>
        private bool IsCollectiveComparisonAhead()
        {
            int lookahead = 1;
            bool hasComma = false;

            while (true)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(lookahead);

                if (type == TokenType.COMMA) hasComma = true;

                if (IsCollectiveOperator(type) && hasComma)
                {
                    return true;
                }

                if (type is TokenType.L_BRACE or TokenType.THIN_ARROW or TokenType.EOF or TokenType.EOL)
                {
                    return false;
                }

                lookahead++;
            }
        }

        /// <summary>
        /// Peeks ahead in the token stream to determine if a broadcast pipe call is coming up.
        /// </summary>
        private bool IsBroadCastPipeFunctionCall()
        {
            int lookahead = 3;
            bool hasUnderscore = false;

            while (true)
            {
                TokenType type = _lexer.PeekTokenTypeAheadByN(lookahead);

                if (type == TokenType.R_PAREN) break; // End of argument list.
                if (type == TokenType.EOL)
                {
                    return false;
                }

                if (type == TokenType.EOF) return false; // End of file, not a valid call.

                if (type == TokenType.UNDERSCORE) hasUnderscore = true;

                lookahead++;
                if (_lexer.PeekTokenTypeAheadByN(lookahead) == TokenType.COMMA)
                {
                    lookahead++;
                }
            }

            // The next token must be a chain assignment operator.
            return hasUnderscore && IsChainAssignmentOperator(_lexer.PeekTokenTypeAheadByN(lookahead + 1));
        }

        /// <summary>
        /// Converts a multi-target compound assignment TokenType into its corresponding arithmetic InstructionCode.
        /// </summary>
        private static InstructionCode GetInstructionCodeForMultiCompoundAssignment(TokenType type) => type switch
        {
            TokenType.DOT_STAR_EQUAL => InstructionCode.Multiply,
            TokenType.DOT_SLASH_EQUAL => InstructionCode.Divide,
            TokenType.DOT_PLUS_EQUAL => InstructionCode.Add,
            TokenType.DOT_MINUS_EQUAL => InstructionCode.Subtract,
            _ => InstructionCode.NotImplemented
        };

        /// <summary>
        /// Checks whether the operator is not a simple comparison operator, rather a complex one like collective comparison.
        /// </summary>
        /// <param name="type">The type of the token.</param>
        private static bool IsNotAStandardComparison(TokenType type)
        {
            return !IsStandardComparisonOperator(type) && type != TokenType.EQUAL_EQUAL && type != TokenType.BANG_EQUAL;
        }

        /// <summary>
        /// Checks if the operator is a collective OR operator.
        /// </summary>
        /// <param name="type">The type of the token.</param>
        /// <returns>True if it is.</returns>
        private static bool IsOrCollectiveOperator(TokenType type) =>
            type is >= TokenType.COLLECTIVE_OR_EQUAL and <= TokenType.COLLECTIVE_OR_GREATER_EQUAL;

        /// <summary>
        /// Converts a collective comparison TokenType into its corresponding base comparison InstructionCode.
        /// </summary>
        /// <param name="type">The TokenType of the collective comparison operator.</param>
        /// <returns>The corresponding base InstructionCode.</returns>
        private static InstructionCode GetInstructionCodeForCollectiveOp(TokenType type) => type switch
        {
            TokenType.COLLECTIVE_EQUAL => InstructionCode.Equal,
            TokenType.COLLECTIVE_NOT_EQUAL => InstructionCode.NotEqual,
            TokenType.COLLECTIVE_GREATER => InstructionCode.GreaterThan,
            TokenType.COLLECTIVE_GREATER_EQUAL => InstructionCode.GreaterEqual,
            TokenType.COLLECTIVE_LESS => InstructionCode.LessThan,
            TokenType.COLLECTIVE_LESS_EQUAL => InstructionCode.LessEqual,
            TokenType.COLLECTIVE_OR_EQUAL => InstructionCode.Equal,
            TokenType.COLLECTIVE_OR_NOT_EQUAL => InstructionCode.NotEqual,
            TokenType.COLLECTIVE_OR_LESS => InstructionCode.LessEqual,
            TokenType.COLLECTIVE_OR_LESS_EQUAL => InstructionCode.LessEqual,
            TokenType.COLLECTIVE_OR_GREATER => InstructionCode.GreaterThan,
            TokenType.COLLECTIVE_OR_GREATER_EQUAL => InstructionCode.GreaterEqual,
            _ => InstructionCode.NotImplemented
        };
    }
}