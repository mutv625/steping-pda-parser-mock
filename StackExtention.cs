using System;
using System.Collections.Generic;
using System.Linq;

public static class StackExtensions
{
    /// <summary>
    /// コレクションの順番で先頭が最も上になるように、スタックにそっと全部を沈める
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="stack"></param>
    /// <param name="collection"></param>
    public static void SinkRange<T>(this Stack<T> stack, IEnumerable<T> collection)
    {
        foreach (var item in collection.Reverse())
        {
            stack.Push(item);
        }
    }
}