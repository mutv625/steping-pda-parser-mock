using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;


#region Token
public interface IToken
{
    object? Value { get; }
}

public interface IToken<T> : IToken
{
    new T Value { get; }
}

public class ValueToken<T> : IToken<T>
{
    public T Value { get; private set; }
    object? IToken.Value => this.Value;

    public ValueToken(T value)
    {
        Value = value;
    }
}
#endregion

public class Parser // : IProcessable
{
    // # パーサーの内部状態
    public ImmutableList<IToken> Tokens { get; private set; }
    public int PtrIndex { get; private set; } = 0;
    public int PtrInc() => ++PtrIndex;

    // # パース待ちスタック
    Stack<IParseTask> _parseTasksStack = new();

    // # パース結果
    public BaseNode ParentNode { get; init; }
    public BaseNode PointingNode { get; set; }
    
    public Parser(List<IToken> tokens, IParseTask initTask, BaseNode initNode)
    {
        Tokens = tokens.ToImmutableList();
        _parseTasksStack.Push(initTask);

        ParentNode = initNode;
        PointingNode = initNode;

        ParentNode.OnVisit();
    }

    public void Process()
    {
        // == プッシュダウンオートマトン的挙動
        _parseTasksStack.SinkRange(_parseTasksStack.Pop().Process());
    }
}


// == ノードの定義
#region Node
public abstract class BaseNode
{
    /// <summary>
    /// 親ノードを取得する
    /// ルートノードの場合はnullを返す
    /// </summary>
    public BaseNode? ParentNode { get; }

    public bool IsVisited { get; protected set; } = false;
    public void MarkAsVisited() => IsVisited = true;

    public bool IsClosed { get; } = false;

    /// <summary>
    /// コンストラクタでは、親ノードの設定と、値の初期化のみを行う
    /// </summary>
    /// <param name="parentNode"></param>
    protected BaseNode(BaseNode? parentNode)
    {
        ParentNode = parentNode;
    }

    public void OnVisit()
    {
        if (!IsVisited)
        {
            OnFirstVisit();
            MarkAsVisited();
        }
    }

    /// <summary>
    /// ノードの初期化処理として、直属の子ノードの生成（コンストラクタ呼び出し）を行う
    /// </summary>
    /// <param name="parentNode"></param>
    protected abstract void OnFirstVisit();

    /// <summary>
    /// JSONとしてノードの内容を文字列化する
    /// </summary>
    /// <returns></returns>
    public abstract string Serialize();
}

public class ValueNode<T> : BaseNode
{
    /// <summary>
    /// このノードが保持する値
    /// 未設定の場合はnullを返す
    /// </summary>
    public T? Value { get; set; }

    public ValueNode(BaseNode parentNode) : base(parentNode)
    {
        Value = default;
    }

    protected override void OnFirstVisit()
    {
        // * 値ノードは初期化時に子ノードを持たないため、特に何もしない
    }

    public override string Serialize()
    {
        return $"{{\"Value\": \"{Value?.ToString() ?? "null"}\"}}";
    }
}

public class ValueListNode<T> : BaseNode
{
    public List<T> Values { get; private set; }

    public ValueListNode(BaseNode parentNode) : base(parentNode)
    {
        Values = new List<T>();
    }

    protected override void OnFirstVisit()
    {
        // * 値リストノードは初期化時に子ノードを持たないため、特に何もしない
    }

    public override string Serialize()
    {
        var serializedValues = string.Join(", ", Values.Select(v => $"\"{v?.ToString() ?? "null"}\""));
        return $"{{\"Values\": [{serializedValues}]}}";
    }
}
#endregion

// == パースを行うタスクの定義
#region Task
/// <summary>
/// 内部にパーサー持とうぜ
/// </summary>
public interface IParseTask
{
    /// <summary>
    /// 1ステップのProcessで実行されるべきこと
    /// </summary>
    /// <returns></returns>
    public List<IParseTask> Process();
}

public abstract class UserTask : IParseTask
{
    protected readonly Parser _parser;
    readonly BaseNode _nodeToVisit;

    /// <param name="nodeToVisit">このタスクのUserProcess()が実行される移動先ノード</param>
    public UserTask(Parser p, BaseNode nodeToVisit)
    {
        _parser = p;
        _nodeToVisit = nodeToVisit;
    }

    public List<IParseTask> Process()
    {
        _parser.PointingNode = _nodeToVisit;
        _parser.PointingNode.OnVisit();

        return UserProcess();
    }

    public abstract List<IParseTask> UserProcess();
}

// TokenType を受け取り、それが保持する値の型 T をコンパイラに特定させる
class ParseValueTask<TokenType, T> : IParseTask 
    where TokenType : IToken<T>
{
    readonly Parser _parser;
    readonly ValueNode<T> _nodeToVisit;

    public ParseValueTask(Parser p, ValueNode<T> nodeToVisit)
    {
        _parser = p;
        _nodeToVisit = nodeToVisit;
    }

    public List<IParseTask> Process()
    {
        _parser.PointingNode = _nodeToVisit;
        _parser.PointingNode.OnVisit();

        return new List<IParseTask>
        {
            new SetValueTask<TokenType, T>(_parser),
            new ReturnTask(_parser)
        };
    }
}

class SetValueTask<TokenType, T> : IParseTask 
    where TokenType : IToken<T>
{
    readonly Parser _parser;

    public SetValueTask(Parser p)
    {
        _parser = p;
    }

    public List<IParseTask> Process()
    {
        IToken currentToken = _parser.Tokens[_parser.PtrIndex];
        
        switch (currentToken)
        {
            // TokenType (ValueToken<T>) であることが保証されているため、型安全に代入可能
            case TokenType tToken when _parser.PointingNode is ValueNode<T> valueNode:
                valueNode.Value = tToken.Value;
                break;
            case TokenType tToken when _parser.PointingNode is ValueListNode<T> valueListNode:
                valueListNode.Values.Add(tToken.Value);
                break;
            case TokenType:
                throw new Exception($"Current pointing node cannot accept the token value. Token value type: {typeof(T).Name}, Pointing node type: {_parser.PointingNode.GetType().Name}");
            default:
                throw new Exception("Unexpected token type. Expected: " + typeof(TokenType).Name + ", but got: " + currentToken.GetType().Name);
        }
        _parser.PtrInc();
        return new List<IParseTask>();
    }
}

class ExpectTokenTask<TokenType> : IParseTask where TokenType : IToken
{
    readonly Parser _parser;

    public ExpectTokenTask(Parser p)
    {
        _parser = p;
    }

    public List<IParseTask> Process()
    {
        IToken currentToken = _parser.Tokens[_parser.PtrIndex];

        if (currentToken is TokenType)
        {
            _parser.PtrInc();
            return new List<IParseTask>();
        }
        else
        {
            throw new Exception("Unexpected token type. Expected: " + typeof(TokenType).Name + ", but got: " + currentToken.GetType().Name);
        }
    }
}

class ReturnTask : IParseTask
{
    readonly Parser _parser;

    public ReturnTask(Parser p)
    {
        _parser = p;
    }

    public List<IParseTask> Process()
    {
        if (_parser.PointingNode.ParentNode == null)
        {
            throw new Exception("Cannot return from the root node. No parent node exists.");
        }

        _parser.PointingNode = _parser.PointingNode.ParentNode;
        return new List<IParseTask>();
    }
}

/// <summary>
/// ポインターを足止めし、ifのように先読みしてパターンを判定するタスク
/// Match終了時に、マッチしたパターンに対応するタスクを返す（ポインター位置は変更されないことに注意）
/// </summary>
class MatchTask : IParseTask
{
    public int Length { get; init; }
    public int ProgressCount { get; private set; }
    public MatchDict TaskMatchDict { get; init; }

    Parser _parser;

    public MatchTask(Parser p, int length, MatchDict matchDict)
    {
        Length = length;
        ProgressCount = 0;
        TaskMatchDict = matchDict;
        _parser = p;
    }

    public List<IParseTask> Process()
    {
        if (ProgressCount >= Length)
        {
            return TaskMatchDict.GetMatchedPattern();
        }
        else if (_parser.PtrIndex + ProgressCount >= _parser.Tokens.Count)
        {
            throw new Exception("Unexpected end of tokens. Expected more tokens to match the pattern.");
        }
        else
        {            
            TaskMatchDict.Match(ProgressCount, _parser.Tokens[_parser.PtrIndex + ProgressCount]);
            ProgressCount++;

            return new List<IParseTask>() { this };
        }
    }
}
#endregion

#region Pattern
class MatchDict
{
    List<LockedPatternList> _patterns;

    public MatchDict(List<LockedPatternList> patterns)
    {
        _patterns = patterns;
    }


    public int RemainingCount
    {
        get => _patterns.Count(p => p.IsCorresponding);
    }

    public void Match(int progressCount, IToken leadingToken)
    {
        // * 1. パターンにマッチしないものは除外
        for (int i = 0; i < _patterns.Count; i++)
        {
            LockedPatternList currentPattern = _patterns[i];
            
            if (!currentPattern.IsCorresponding) continue;
            if (currentPattern.Length <= progressCount) continue;

            if (!currentPattern[progressCount].IsInstanceOfType(leadingToken))
            {
                currentPattern.IsCorresponding = false;
            }
        }
    }

    public List<IParseTask> GetMatchedPattern()
    {
        // * フォールスルーパターン (remaining = 0 になったときの特殊処理) + 優先度
        // Anyが一番下にあれば、Firstで取ってくれば全パターンに対応できる
        if (_patterns.Count == 0)
        {
            throw new Exception("Remaining Patterns is empty. This is a fall-through pattern. Please check the grammar.");
        }
        else
        {
            return _patterns.First(p => p.IsCorresponding).FollowingParseTasks;
        }
    }
}


public class LockedPatternList
{
    private LockedPatternList(List<Type> patterns, List<IParseTask> followingParseTasks)
    {
        _inner = patterns.ToList();
        FollowingParseTasks = followingParseTasks.ToList();
        Length = _inner.Count;
        IsCorresponding = true;
    }

    internal static LockedPatternList Create(List<Type> patterns, List<IParseTask> followingParseTasks)
    {
        return new LockedPatternList(patterns, followingParseTasks);
    }

    readonly List<Type> _inner;
    
    public bool IsCorresponding { get; set; }

    public int Length { get; }
    public Type this[int value]
    {
        get => _inner[value];
    }

    public List<IParseTask> FollowingParseTasks { get; }
}

public class PatternList
{
    readonly List<Type> _inner = new();

    public PatternList()
    {
        
    }

    public PatternList AddPattern<T>() where T : IToken
    {
        _inner.Add(typeof(T));
        return this;
    }

    public LockedPatternList FollowedBy(List<IParseTask> followingParseTasks)
    {
        return LockedPatternList.Create(_inner, followingParseTasks);
    }
}
#endregion