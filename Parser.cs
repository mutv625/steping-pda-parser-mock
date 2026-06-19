using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

public interface IToken {}

public class Parser // : IProcessable
{
    // # パーサーの内部状態
    public ImmutableList<IToken> Tokens { get; private set; }
    public int PrtIndex { get; private set; } = 0;

    // # パース待ちスタック
    Stack<IParseTask> _parseTasksStack = new();

    // # パース結果
    public INode ParentNode { get; init; }
    public INode PointingNode { get; private set; }
    
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
        IParseTask task = _parseTasksStack.Pop();

        switch (task)
        {
            case MatchTask matchTask
            when matchTask.MatchingDict.RemainingCount == 1 || matchTask.Length == matchTask.ProgressCount:
                _parseTasksStack.SinkRange(matchTask.MatchingDict.GetMatchedPattern());
                break;
            case ReturnTask returnTask:

                break;
            case ConsumeTask consumeTask:

                break;
            default:
                List<IParseTask> retTasks = task.Process();
                _parseTasksStack.SinkRange(retTasks);
                break;
        }
    }
}

// == ノードの定義
public interface INode
{
    public INode ParentNode { get; }
    public bool IsClosed { get; }
    public string Serialize();
}

public interface ITerminal<T> : INode
{
    public T Value { get; }
    public void Set(T element);
}

public interface IPolyTerminal<T> : ITerminal<T>
{
    public List<T> Values { get; }
    new public void Set(T element);
}

// == パースを待つブロックの定義
public interface IParseTask
{
    /// <summary>
    /// 1ステップのProcessで実行されるべきこと
    /// </summary>
    /// <returns></returns>
    public List<IParseTask> Process();
}


class MatchTask : IParseTask
{
    public int Length { get; init; }
    public int ProgressCount { get; private set; }
    public MatchDict MatchingDict { get; init; }

    Parser _parser;

    public MatchTask(int length, MatchDict matchDict, Parser self)
    {
        Length = length;
        ProgressCount = 0;
        MatchingDict = matchDict;
        _parser = self;
    }

    public List<IParseTask> Process()
    {
        MatchingDict.Match(ProgressCount, _parser.Tokens[_parser.PrtIndex]);
        ProgressCount++;

        return new List<IParseTask>(){this};
    }
}

class ConsumeTask : IParseTask
{
    public List<IParseTask> Process()
    {
        // 形だけ
        return new List<IParseTask>();
    }
}

class ReturnTask : IParseTask
{
    public List<IParseTask> Process()
    {
        // 形だけ
        return new List<IParseTask>();
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
