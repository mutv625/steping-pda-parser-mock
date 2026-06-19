using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

public interface IToken
{
    object? Value { get; }
}

public interface IToken<T> : IToken
{
    new T Value { get; }
}

public class Token<T> : IToken<T>
{
    public T Value { get; private set; }
    object? IToken.Value => this.Value;

    public Token(T value)
    {
        Value = value;
    }
}

public class Parser // : IProcessable
{
    // # パーサーの内部状態
    public ImmutableList<IToken> Tokens { get; private set; }
    public int PrtIndex { get; private set; } = 0;
    public int PtrInc() => ++PrtIndex;

    // # パース待ちスタック
    Stack<IParseTask> _parseTasksStack = new();

    // # パース結果
    public INode ParentNode { get; init; }
    public INode PointingNode { get; set; }
    
    public Parser(List<IToken> tokens, IParseTask initTask, INode initNode)
    {
        Tokens = tokens.ToImmutableList();
        _parseTasksStack.Push(initTask);

        ParentNode = initNode;
        PointingNode = initNode;
    }

    public void Process()
    {
        // == プッシュダウンオートマトン的挙動
        _parseTasksStack.SinkRange(_parseTasksStack.Pop().Process());
    }
}

// == ノードの定義
public interface INode
{
    public INode ParentNode { get; }
    public bool IsClosed { get; }

    /// <summary>
    /// ノードの初期化処理として、親ノードを設定し、直属の子ノードの生成を行う。
    /// （コンストラクタはこのノードの生成のみ、InitializeNodeメソッドで初期化処理を行う）
    /// </summary>
    /// <param name="parentNode"></param>
    public void InitializeNode(INode parentNode);
    public string Serialize();
}

public interface IValueNode<T> : INode
{
    public T Value { get; set; }
}

// == パースを待つブロックの定義
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
    Parser _parser;
    INode _gotoNode;

    public UserTask(Parser parser, INode gotoNode)
    {
        _parser = parser;
        _gotoNode = gotoNode;
    }

    public List<IParseTask> Process()
    {
        _parser.PointingNode = _gotoNode;
        return UserProcess();
    }

    public abstract List<IParseTask> UserProcess();
}

class SetValueTask<T> : IParseTask
{
    Parser _parser;

    public SetValueTask(Parser self)
    {
        _parser = self;
    }

    public List<IParseTask> Process()
    {
        IToken currentToken = _parser.Tokens[_parser.PrtIndex];
        
        switch (currentToken)
        {
            case Token<T> tToken when _parser.PointingNode is IValueNode<T> valueNode:
                valueNode.Value = tToken.Value;
                break;
            default:
                throw new Exception("Unexpected token type. Expected: " + typeof(T).Name + ", but got: " + currentToken.GetType().Name);
        }
        _parser.PtrInc();
        return new List<IParseTask>() {new ReturnTask(_parser)};
    }
}

class ReturnTask : IParseTask
{
    Parser _parser;

    public ReturnTask(Parser self)
    {
        _parser = self;
    }

    public List<IParseTask> Process()
    {
        _parser.PointingNode = _parser.PointingNode.ParentNode;
        return new List<IParseTask>();
    }
}

// ! Goto!! やばそ～
class GotoTask : IParseTask
{
    Parser _parser;
    INode _targetNode;

    public GotoTask(INode targetNode, Parser self)
    {
        _targetNode = targetNode;
        _parser = self;
    }

    public List<IParseTask> Process()
    {
        _parser.PointingNode = _targetNode;
        return new List<IParseTask>();
    }
}

class MatchTask : IParseTask
{
    public int Length { get; init; }
    public int ProgressCount { get; private set; }
    public MatchDict TaskMatchDict { get; init; }

    Parser _parser;

    public MatchTask(int length, MatchDict matchDict, Parser self)
    {
        Length = length;
        ProgressCount = 0;
        TaskMatchDict = matchDict;
        _parser = self;
    }

    public List<IParseTask> Process()
    {
        if (ProgressCount >= Length)
        {
            return TaskMatchDict.GetMatchedPattern();
        }
        else if (_parser.PrtIndex + ProgressCount >= _parser.Tokens.Count)
        {
            throw new Exception("Unexpected end of tokens. Expected more tokens to match the pattern.");
        }
        else
        {            
            TaskMatchDict.Match(ProgressCount, _parser.Tokens[_parser.PrtIndex + ProgressCount]);
            ProgressCount++;

            return new List<IParseTask>() { this };
        }
    }
}


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
