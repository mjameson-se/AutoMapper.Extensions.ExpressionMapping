using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AutoMapper.Internal;

namespace AutoMapper
{
    using AutoMapper.Extensions.ExpressionMapping;
    using static Expression;

    internal static class ExpressionExtensions
    {
        public static Expression MemberAccesses(this IEnumerable<MemberInfo> members, Expression obj) =>
            members.Aggregate(obj, (expression, member) => MakeMemberAccess(expression, member));

        public static IEnumerable<MemberExpression> GetMembers(this Expression expression)
        {
            var memberExpression = expression as MemberExpression;
            if(memberExpression == null)
            {
                return new MemberExpression[0];
            }
            return memberExpression.GetMembers();
        }

        public static IEnumerable<MemberExpression> GetMembers(this MemberExpression expression)
        {
            while(expression != null)
            {
                yield return expression;
                expression = expression.Expression as MemberExpression;
            }
        }

        public static bool IsMemberPath(this LambdaExpression exp)
        {
            return exp.Body.GetMembers().LastOrDefault()?.Expression == exp.Parameters.First();
        }

        private static bool IsEffectivelyConstant(this Expression exp, out ConstantExpression constant, out bool isParameter)
        {
            if (exp is ConstantExpression cnst)
            {
                constant = cnst;
                isParameter = false;
                return true;
            }
            else if (exp is MemberExpression member && member.Expression is ConstantExpression memberConst)
            {
                constant = memberConst;
                isParameter = true;
                return true;
            }
            constant = null;
            isParameter = false;
            return false;
        }

        public static bool TryMappingConstant(this Expression exp, IMapper mapper, Type newType, out Expression newExp)
        {
            if (exp.IsEffectivelyConstant(out ConstantExpression constant, out bool isParameter))
            {
                if (constant.Type == newType)
                {
                    newExp = exp;
                    return true;
                }
                object newValue = mapper.MapObject(ExpressionHelpers.Unbox(constant.Value), exp.Type, newType);
                newExp = ExpressionHelpers.BuildConstant(newValue, newType, isParameter);
                return true;
            }
            newExp = null;
            return false;
        }
    }

    internal static class ExpressionHelpers
    {
        public static MemberExpression MemberAccesses(string members, Expression obj) =>
            (MemberExpression)GetMemberPath(obj.Type, members).MemberAccesses(obj);

        public static Expression ReplaceParameters(this LambdaExpression exp, params Expression[] replace)
        {
            var replaceExp = exp.Body;
            for (var i = 0; i < Math.Min(replace.Length, exp.Parameters.Count); i++)
                replaceExp = Replace(replaceExp, exp.Parameters[i], replace[i]);
            return replaceExp;
        }

        public static Expression Replace(this Expression exp, Expression old, Expression replace) => new ReplaceExpressionVisitor(old, replace).Visit(exp);

        private static IEnumerable<MemberInfo> GetMemberPath(Type type, string fullMemberName)
        {
            MemberInfo property = null;
            foreach (var memberName in fullMemberName.Split('.'))
            {
                var currentType = GetCurrentType(property, type);
                yield return property = currentType.GetFieldOrProperty(memberName);
            }
        }

        private static Type GetCurrentType(MemberInfo member, Type type)
            => member?.GetMemberType() ?? type;

        #region Boxing
        private interface IBox
        {
            public object GetValue();
        }

        private sealed class StronglyTypedBox<T> : IBox
        {
            public StronglyTypedBox(T value) // Used via reflection
            {
                Value = value;
            }

            public T Value { get; }

            public object GetValue() => Value;
        }

        public static object Unbox(object mightBeBoxed) => mightBeBoxed is IBox box ? box.GetValue() : mightBeBoxed;
        #endregion

        /// <summary>
        /// Builds a constant expression from the given value with the target type. In some cases, 
        /// for maximal interoperability with EntityFramework, it is preferred to translate member references
        /// into member references (referencing arbitrary "boxing" instances) because EF will cache expression
        /// compilation more efficiently if the constants are not inlined.
        /// </summary>
        /// <param name="constantValue">the value for the constant</param>
        /// <param name="constantType">the type of the constant, necessary when the value is null.</param>
        /// <param name="isMemberReference">if true, a member reference is constructed to wrap the constant</param>
        /// <returns>an expression representing the new constant expression.</returns>
        public static Expression BuildConstant(object constantValue, Type constantType, bool isMemberReference)
        {
            if (isMemberReference)
            {
                object holder = typeof(StronglyTypedBox<>).MakeGenericType(constantType).GetConstructor(new[] { constantType }).Invoke(new[] { constantValue });
                return Property(Constant(holder), nameof(StronglyTypedBox<object>.Value));
            }
            return Constant(constantValue, constantType);
        }
    }
}