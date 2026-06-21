using System;
using System.Linq;

public static class TypeExtensions
{
    public static string GetPrettyName(this Type type)
    {
        if (type.IsGenericType)
        {
            var genericArgs = type.GetGenericArguments().Select(t => t.GetPrettyName());
            var name = type.GetGenericTypeDefinition().Name;
            // ` の前の部分を取得
            name = name.Substring(0, name.IndexOf('`'));
            return $"{name}<{string.Join(", ", genericArgs)}>";
        }
        return type.Name;
    }
}
