using System.Linq.Expressions;
using System.Reflection;
using Unreal.Core.Contracts;

namespace MoreFortniteReplayDecompression.Reading;

/// <summary>
/// Tells whether an export group carries data: at least one property that is not <c>null</c> (reference and nullable types)
/// or not <c>default</c> (other value types). The parser resets a reused instance, so this means "was sent in this update".
/// Checks are compiled once per type.
/// </summary>
internal static class Presence
{
    private static readonly Dictionary<(Type, bool), Func<object, bool>> Cache = new();
    private static readonly MethodInfo ObjectEquals = typeof(object).GetMethod(nameof(Equals), [typeof(object)])!;

    public static bool Any(INetFieldExportGroup group, HashSet<string>? exclude)
    {
        (Type, bool) key = (group.GetType(), exclude is not null);
        if (!Cache.TryGetValue(key, out Func<object, bool>? check))
        {
            check = Build(key.Item1, exclude);
            Cache[key] = check;
        }

        return check(group);
    }

    private static Func<object, bool> Build(Type type, HashSet<string>? exclude)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(object), "group");
        UnaryExpression typed = Expression.Convert(parameter, type);
        Expression? body = null;

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (exclude?.Contains(property.Name) == true)
            {
                continue;
            }

            MemberExpression access = Expression.Property(typed, property);
            Type propertyType = property.PropertyType;
            Expression test;

            if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
            {
                test = Expression.NotEqual(access, Expression.Constant(null, propertyType));
            }
            else if (propertyType.IsPrimitive || propertyType.IsEnum)
            {
                test = Expression.NotEqual(access, Expression.Default(propertyType));
            }
            else
            {
                test = Expression.Not(Expression.Call(
                    Expression.Convert(access, typeof(object)), ObjectEquals, Expression.Convert(Expression.Default(propertyType), typeof(object))));
            }

            body = body is null ? test : Expression.OrElse(body, test);
        }

        return body is null ? _ => false : Expression.Lambda<Func<object, bool>>(body, parameter).Compile();
    }
}
