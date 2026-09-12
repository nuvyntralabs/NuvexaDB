using System.Linq.Expressions;

namespace Nuventra.NuvexaDB.Query;

/// <summary>Translates a subset of LINQ expressions to <see cref="NuvexaFilter"/> without compiling (AOT-safe visitor).</summary>
public static class ExpressionFilter
{
    public static NuvexaFilter From<T>(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Visit(predicate.Body);
    }

    private static NuvexaFilter Visit(Expression expr)
    {
        switch (expr)
        {
            case BinaryExpression bin:
                if (bin.NodeType == ExpressionType.AndAlso)
                {
                    return NuvexaFilter.And(Visit(bin.Left), Visit(bin.Right));
                }

                if (bin.NodeType == ExpressionType.OrElse)
                {
                    return NuvexaFilter.Or(Visit(bin.Left), Visit(bin.Right));
                }

                return VisitCompare(bin);
            case UnaryExpression { NodeType: ExpressionType.Not } not:
                throw new NuvexaException("Use explicit $ne / $nin filters; boolean Not is not translated.");
            case MethodCallExpression call when call.Method.Name == "StartsWith":
                var path = MemberPath(call.Object!);
                var prefix = Eval(call.Arguments[0])?.ToString() ?? "";
                return NuvexaFilter.Regex(path, "^" + System.Text.RegularExpressions.Regex.Escape(prefix));
            default:
                throw new NuvexaException($"Unsupported LINQ expression '{expr.NodeType}'.");
        }
    }

    private static NuvexaFilter VisitCompare(BinaryExpression bin)
    {
        var (member, value, flipped) = Extract(bin);
        var path = MemberPath(member);
        var boxed = Eval(value);
        return bin.NodeType switch
        {
            ExpressionType.Equal => NuvexaFilter.Eq(path, boxed),
            ExpressionType.NotEqual => NuvexaFilter.Ne(path, boxed),
            ExpressionType.GreaterThan => flipped ? NuvexaFilter.Lt(path, boxed!) : NuvexaFilter.Gt(path, boxed!),
            ExpressionType.GreaterThanOrEqual => flipped ? NuvexaFilter.Lte(path, boxed!) : NuvexaFilter.Gte(path, boxed!),
            ExpressionType.LessThan => flipped ? NuvexaFilter.Gt(path, boxed!) : NuvexaFilter.Lt(path, boxed!),
            ExpressionType.LessThanOrEqual => flipped ? NuvexaFilter.Gte(path, boxed!) : NuvexaFilter.Lte(path, boxed!),
            _ => throw new NuvexaException($"Unsupported comparison '{bin.NodeType}'.")
        };
    }

    private static (Expression Member, Expression Value, bool Flipped) Extract(BinaryExpression bin)
    {
        if (IsMember(bin.Left))
        {
            return (bin.Left, bin.Right, false);
        }

        if (IsMember(bin.Right))
        {
            return (bin.Right, bin.Left, true);
        }

        throw new NuvexaException("LINQ comparisons must involve a document member.");
    }

    private static bool IsMember(Expression expr) =>
        expr is MemberExpression or UnaryExpression { Operand: MemberExpression };

    private static string MemberPath(Expression expr)
    {
        if (expr is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            expr = u.Operand;
        }

        var parts = new Stack<string>();
        while (expr is MemberExpression member)
        {
            parts.Push(ToCamel(member.Member.Name));
            expr = member.Expression!;
        }

        return string.Join('.', parts);
    }

    private static string ToCamel(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static object? Eval(Expression expr)
    {
        if (expr is ConstantExpression c)
        {
            return c.Value;
        }

        var lambda = Expression.Lambda(expr);
        return lambda.Compile().DynamicInvoke();
    }
}
