using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotNetAntiSlop.Analyzers;

internal static class AnalyzerUtilities
{
    private static readonly string[] ReadOnlyPrefixes =
    {
        "Get", "Find", "Read", "List", "Search", "Query", "Load", "Fetch", "Browse"
    };

    internal static IMethodSymbol? GetInvokedMethod(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
    }

    internal static IInvocationOperation? GetInvocationOperation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetOperation(invocation, cancellationToken) as IInvocationOperation;
    }

    internal static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditional)
        {
            return conditional.Expression;
        }

        return null;
    }

    internal static bool IsNamespace(ISymbol? symbol, string namespaceName)
    {
        if (symbol == null)
        {
            return false;
        }

        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
        return string.Equals(containingNamespace, namespaceName, StringComparison.Ordinal);
    }

    internal static bool IsNamespaceOrChild(ISymbol? symbol, string namespacePrefix)
    {
        if (symbol == null)
        {
            return false;
        }

        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == null)
        {
            return false;
        }

        return string.Equals(containingNamespace, namespacePrefix, StringComparison.Ordinal) ||
               containingNamespace.StartsWith(namespacePrefix + ".", StringComparison.Ordinal);
    }

    internal static bool IsType(ITypeSymbol? type, string namespaceName, string typeName)
    {
        var named = type as INamedTypeSymbol;
        if (named == null)
        {
            return false;
        }

        var original = named.OriginalDefinition;
        return string.Equals(original.Name, typeName, StringComparison.Ordinal) &&
               string.Equals(original.ContainingNamespace?.ToDisplayString(), namespaceName, StringComparison.Ordinal);
    }

    internal static bool IsTaskLike(ITypeSymbol? type)
    {
        var named = type as INamedTypeSymbol;
        if (named == null)
        {
            return false;
        }

        var original = named.OriginalDefinition;
        if (!string.Equals(original.ContainingNamespace?.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(original.Name, "Task", StringComparison.Ordinal) ||
               string.Equals(original.Name, "ValueTask", StringComparison.Ordinal);
    }

    internal static bool IsValueTask(ITypeSymbol? type)
    {
        return IsType(type, "System.Threading.Tasks", "ValueTask");
    }

    internal static bool IsCancellationToken(ITypeSymbol? type)
    {
        return IsType(type, "System.Threading", "CancellationToken");
    }

    internal static bool IsDbContext(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (IsType(current, "Microsoft.EntityFrameworkCore", "DbContext"))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsDbSet(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (IsType(current, "Microsoft.EntityFrameworkCore", "DbSet"))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsOrImplements(
        ITypeSymbol? type,
        string namespaceName,
        string typeName)
    {
        if (type == null)
        {
            return false;
        }

        if (IsType(type, namespaceName, typeName))
        {
            return true;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (IsType(implemented, namespaceName, typeName))
            {
                return true;
            }
        }

        return false;
    }

    internal static ITypeSymbol? GetSequenceElementType(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }

        var named = type as INamedTypeSymbol;
        if (named == null)
        {
            return null;
        }

        if (named.TypeArguments.Length == 1 &&
            (IsType(named, "System.Collections.Generic", "IEnumerable") ||
             IsType(named, "System.Linq", "IQueryable") ||
             IsType(named, "Microsoft.EntityFrameworkCore", "DbSet") ||
             IsType(named, "System.Collections.Generic", "IAsyncEnumerable")))
        {
            return named.TypeArguments[0];
        }

        foreach (var implemented in named.AllInterfaces)
        {
            if (implemented.TypeArguments.Length == 1 &&
                (IsType(implemented, "System.Collections.Generic", "IEnumerable") ||
                 IsType(implemented, "System.Linq", "IQueryable") ||
                 IsType(implemented, "System.Collections.Generic", "IAsyncEnumerable")))
            {
                return implemented.TypeArguments[0];
            }
        }

        return null;
    }

    internal static bool IsMaterializedCollection(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (IsOrImplements(type, "System.Collections", "ICollection") ||
            IsOrImplements(type, "System.Collections.Generic", "ICollection") ||
            IsOrImplements(type, "System.Collections.Generic", "IReadOnlyCollection"))
        {
            return true;
        }

        return false;
    }

    internal static bool IsLazySequence(ITypeSymbol? type)
    {
        if (type == null || IsMaterializedCollection(type))
        {
            return false;
        }

        return IsOrImplements(type, "System.Collections.Generic", "IEnumerable") ||
               IsOrImplements(type, "System.Linq", "IQueryable") ||
               IsOrImplements(type, "System.Collections.Generic", "IAsyncEnumerable");
    }

    internal static bool IsInsideLoop(SyntaxNode node)
    {
        return node.Ancestors().Any(
            ancestor => ancestor is ForStatementSyntax ||
                        ancestor is ForEachStatementSyntax ||
                        ancestor is ForEachVariableStatementSyntax ||
                        ancestor is WhileStatementSyntax ||
                        ancestor is DoStatementSyntax);
    }

    internal static bool IsInsideAsync(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var method = ancestor as MethodDeclarationSyntax;
            if (method != null)
            {
                return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
            }

            var localFunction = ancestor as LocalFunctionStatementSyntax;
            if (localFunction != null)
            {
                return localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);
            }

            var anonymous = ancestor as AnonymousFunctionExpressionSyntax;
            if (anonymous != null)
            {
                return !anonymous.AsyncKeyword.IsKind(SyntaxKind.None);
            }
        }

        return false;
    }

    internal static IParameterSymbol? GetAvailableCancellationToken(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var method = ancestor as MethodDeclarationSyntax;
            if (method != null)
            {
                foreach (var parameter in method.ParameterList.Parameters)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    if (symbol != null && IsCancellationToken(symbol.Type))
                    {
                        return symbol;
                    }
                }

                return null;
            }

            var localFunction = ancestor as LocalFunctionStatementSyntax;
            if (localFunction != null)
            {
                foreach (var parameter in localFunction.ParameterList.Parameters)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    if (symbol != null && IsCancellationToken(symbol.Type))
                    {
                        return symbol;
                    }
                }

                return null;
            }

            var parenthesized = ancestor as ParenthesizedLambdaExpressionSyntax;
            if (parenthesized != null)
            {
                foreach (var parameter in parenthesized.ParameterList.Parameters)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    if (symbol != null && IsCancellationToken(symbol.Type))
                    {
                        return symbol;
                    }
                }

                return null;
            }

            var simple = ancestor as SimpleLambdaExpressionSyntax;
            if (simple != null)
            {
                var symbol = semanticModel.GetDeclaredSymbol(simple.Parameter, cancellationToken);
                if (symbol != null && IsCancellationToken(symbol.Type))
                {
                    return symbol;
                }

                return null;
            }

            var anonymousMethod = ancestor as AnonymousMethodExpressionSyntax;
            if (anonymousMethod != null)
            {
                if (anonymousMethod.ParameterList != null)
                {
                    foreach (var parameter in anonymousMethod.ParameterList.Parameters)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                        if (symbol != null && IsCancellationToken(symbol.Type))
                        {
                            return symbol;
                        }
                    }
                }

                return null;
            }
        }

        return null;
    }

    internal static bool HasCancellationTokenParameter(
        SyntaxNode callable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var method = callable as MethodDeclarationSyntax;
        if (method != null)
        {
            return method.ParameterList.Parameters.Any(
                parameter =>
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    return symbol != null && IsCancellationToken(symbol.Type);
                });
        }

        var localFunction = callable as LocalFunctionStatementSyntax;
        if (localFunction != null)
        {
            return localFunction.ParameterList.Parameters.Any(
                parameter =>
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    return symbol != null && IsCancellationToken(symbol.Type);
                });
        }

        var parenthesized = callable as ParenthesizedLambdaExpressionSyntax;
        if (parenthesized != null)
        {
            return parenthesized.ParameterList.Parameters.Any(
                parameter =>
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    return symbol != null && IsCancellationToken(symbol.Type);
                });
        }

        var simple = callable as SimpleLambdaExpressionSyntax;
        if (simple != null)
        {
            var symbol = semanticModel.GetDeclaredSymbol(simple.Parameter, cancellationToken);
            return symbol != null && IsCancellationToken(symbol.Type);
        }

        var anonymous = callable as AnonymousMethodExpressionSyntax;
        if (anonymous != null && anonymous.ParameterList != null)
        {
            return anonymous.ParameterList.Parameters.Any(
                parameter =>
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
                    return symbol != null && IsCancellationToken(symbol.Type);
                });
        }

        return false;
    }

    internal static bool IsAsyncCallable(
        SyntaxNode callable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var method = callable as MethodDeclarationSyntax;
        if (method != null)
        {
            if (method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                return true;
            }

            var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
            return symbol != null && IsTaskLike(symbol.ReturnType);
        }

        var localFunction = callable as LocalFunctionStatementSyntax;
        if (localFunction != null)
        {
            if (localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                return true;
            }

            var symbol = semanticModel.GetDeclaredSymbol(localFunction, cancellationToken) as IMethodSymbol;
            return symbol != null && IsTaskLike(symbol.ReturnType);
        }

        var anonymous = callable as AnonymousFunctionExpressionSyntax;
        if (anonymous != null)
        {
            if (!anonymous.AsyncKeyword.IsKind(SyntaxKind.None))
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(anonymous, cancellationToken);
            var delegateType = typeInfo.ConvertedType as INamedTypeSymbol;
            return delegateType?.DelegateInvokeMethod != null &&
                   IsTaskLike(delegateType.DelegateInvokeMethod.ReturnType);
        }

        return false;
    }

    internal static bool IsTestMethod(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
        if (symbol == null)
        {
            return false;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null)
            {
                continue;
            }

            var name = attributeClass.Name;
            var ns = attributeClass.ContainingNamespace?.ToDisplayString();
            if ((ns == "Xunit" && (name == "FactAttribute" || name == "TheoryAttribute")) ||
                (ns == "NUnit.Framework" &&
                 (name == "TestAttribute" || name == "TestCaseAttribute" || name == "TestCaseSourceAttribute")) ||
                (ns == "Microsoft.VisualStudio.TestTools.UnitTesting" &&
                 (name == "TestMethodAttribute" || name == "DataTestMethodAttribute")))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsControllerAction(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
        if (symbol == null || symbol.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (InheritsFrom(symbol.ContainingType, "Microsoft.AspNetCore.Mvc", "ControllerBase"))
        {
            return true;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType == null)
            {
                continue;
            }

            var ns = attributeType.ContainingNamespace?.ToDisplayString();
            if (ns == "Microsoft.AspNetCore.Mvc" &&
                (attributeType.Name.StartsWith("Http", StringComparison.Ordinal) ||
                 attributeType.Name == "RouteAttribute" ||
                 attributeType.Name == "ApiControllerAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool InheritsFrom(
        ITypeSymbol? type,
        string namespaceName,
        string typeName)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (IsType(current, namespaceName, typeName))
            {
                return true;
            }
        }

        return false;
    }

    internal static InvocationExpressionSyntax? GetEndpointMappingInvocation(
        AnonymousFunctionExpressionSyntax anonymous)
    {
        SyntaxNode? current = anonymous;
        while (current != null && !(current is ArgumentSyntax))
        {
            current = current.Parent;
        }

        var argument = current as ArgumentSyntax;
        return argument?.Parent?.Parent as InvocationExpressionSyntax;
    }

    internal static bool IsEndpointHandler(
        AnonymousFunctionExpressionSyntax anonymous,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var invocation = GetEndpointMappingInvocation(anonymous);
        if (invocation == null)
        {
            return false;
        }

        var symbol = GetInvokedMethod(semanticModel, invocation, cancellationToken);
        if (symbol == null || !IsNamespaceOrChild(symbol, "Microsoft.AspNetCore"))
        {
            return false;
        }

        return symbol.Name.StartsWith("Map", StringComparison.Ordinal) ||
               symbol.Name == "Use" ||
               symbol.Name == "Run";
    }

    internal static bool IsRequestHandler(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var anonymous = ancestor as AnonymousFunctionExpressionSyntax;
            if (anonymous != null)
            {
                return IsEndpointHandler(
                    anonymous,
                    semanticModel,
                    cancellationToken);
            }

            var method = ancestor as MethodDeclarationSyntax;
            if (method != null)
            {
                return IsControllerAction(
                    method,
                    semanticModel,
                    cancellationToken);
            }
        }

        return false;
    }

    internal static bool IsReadOnlyRequestHandler(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var anonymous = ancestor as AnonymousFunctionExpressionSyntax;
            if (anonymous != null)
            {
                return IsReadOnlyEndpointHandler(
                    anonymous,
                    semanticModel,
                    cancellationToken);
            }

            var method = ancestor as MethodDeclarationSyntax;
            if (method != null)
            {
                if (!IsControllerAction(
                    method,
                    semanticModel,
                    cancellationToken))
                {
                    return false;
                }

                var methodSymbol = semanticModel.GetDeclaredSymbol(
                    method,
                    cancellationToken);
                if (methodSymbol != null)
                {
                    foreach (var attribute in methodSymbol.GetAttributes())
                    {
                        var attributeType = attribute.AttributeClass;
                        if (attributeType != null &&
                            attributeType.ContainingNamespace?.ToDisplayString() ==
                                "Microsoft.AspNetCore.Mvc" &&
                            (attributeType.Name == "HttpGetAttribute" ||
                             attributeType.Name == "HttpHeadAttribute"))
                        {
                            return true;
                        }
                    }
                }

                return IsReadOnlyMethod(method);
            }
        }

        return false;
    }

    private static bool IsReadOnlyEndpointHandler(
        AnonymousFunctionExpressionSyntax anonymous,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var invocation = GetEndpointMappingInvocation(anonymous);
        if (invocation == null)
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(
            invocation,
            cancellationToken);
        var symbol = symbolInfo.Symbol as IMethodSymbol;
        if (symbol == null ||
            !IsNamespaceOrChild(symbol, "Microsoft.AspNetCore"))
        {
            return false;
        }

        if (symbol.Name == "MapGet" || symbol.Name == "MapHead")
        {
            return true;
        }

        if (symbol.Name == "MapMethods")
        {
            foreach (var literal in invocation.ArgumentList.Arguments
                         .SelectMany(
                             argument =>
                                 argument.Expression.DescendantNodesAndSelf())
                         .OfType<LiteralExpressionSyntax>())
            {
                var value = semanticModel.GetConstantValue(
                    literal,
                    cancellationToken);
                var verb = value.HasValue ? value.Value as string : null;
                if (string.Equals(
                        verb,
                        "GET",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        verb,
                        "HEAD",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsReadOnlyMethod(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.ValueText;
        if (!ReadOnlyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        return !method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(
            invocation =>
            {
                var invokedName = GetInvocationName(invocation);
                return invokedName == "SaveChanges" ||
                       invokedName == "SaveChangesAsync" ||
                       invokedName == "ExecuteUpdate" ||
                       invokedName == "ExecuteUpdateAsync" ||
                       invokedName == "ExecuteDelete" ||
                       invokedName == "ExecuteDeleteAsync";
            });
    }

    internal static bool IsReadOnlyContext(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (IsReadOnlyRequestHandler(node, semanticModel, cancellationToken))
        {
            return true;
        }

        var method = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method != null && IsReadOnlyMethod(method);
    }

    internal static string? GetInvocationName(InvocationExpressionSyntax invocation)
    {
        var member = invocation.Expression as MemberAccessExpressionSyntax;
        if (member != null)
        {
            return member.Name.Identifier.ValueText;
        }

        var identifier = invocation.Expression as IdentifierNameSyntax;
        return identifier?.Identifier.ValueText;
    }

    internal static bool IsEfCoreMethod(IMethodSymbol? method)
    {
        if (method == null)
        {
            return false;
        }

        var candidate = method.ReducedFrom ?? method;
        return IsNamespaceOrChild(candidate, "Microsoft.EntityFrameworkCore");
    }

    internal static bool IsLinqMethod(IMethodSymbol? method)
    {
        if (method == null)
        {
            return false;
        }

        var candidate = method.ReducedFrom ?? method;
        return IsNamespace(candidate, "System.Linq");
    }

    internal static bool ContainsInvocationNamed(ExpressionSyntax expression, params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        return expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                var name = GetInvocationName(invocation);
                return name != null && set.Contains(name);
            });
    }

    internal static bool ContainsEfSource(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            var type = semanticModel.GetTypeInfo(candidate, cancellationToken).Type;
            if (IsDbSet(type))
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(candidate, cancellationToken).Symbol;
            if (symbol is IPropertySymbol property && IsDbSet(property.Type))
            {
                return true;
            }
        }

        return false;
    }

    internal static ITypeSymbol? FindDbSetEntityType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            var type = semanticModel.GetTypeInfo(candidate, cancellationToken).Type as INamedTypeSymbol;
            if (type != null && IsDbSet(type) && type.TypeArguments.Length == 1)
            {
                return type.TypeArguments[0];
            }

            var symbol = semanticModel.GetSymbolInfo(candidate, cancellationToken).Symbol as IPropertySymbol;
            var propertyType = symbol?.Type as INamedTypeSymbol;
            if (propertyType != null && IsDbSet(propertyType) && propertyType.TypeArguments.Length == 1)
            {
                return propertyType.TypeArguments[0];
            }
        }

        return null;
    }

    internal static ISymbol? FindDbContextRootSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            ITypeSymbol? type = null;
            if (symbol is ILocalSymbol local)
            {
                type = local.Type;
            }
            else if (symbol is IParameterSymbol parameter)
            {
                type = parameter.Type;
            }
            else if (symbol is IFieldSymbol field)
            {
                type = field.Type;
            }
            else if (symbol is IPropertySymbol property)
            {
                type = property.Type;
            }

            if (IsDbContext(type))
            {
                return symbol;
            }
        }

        foreach (var memberAccess in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            if (receiverSymbol != null && IsDbContext(receiverType))
            {
                return receiverSymbol;
            }
        }

        return null;
    }

    internal static bool HasDevelopmentGuard(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var ifStatement in invocation.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ConditionMeansDevelopment(ifStatement.Condition, semanticModel, cancellationToken, expected: true))
            {
                return true;
            }
        }

        var statement = invocation.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        var block = statement?.Parent as BlockSyntax;
        if (statement == null || block == null)
        {
            return false;
        }

        foreach (var prior in block.Statements)
        {
            if (prior == statement)
            {
                break;
            }

            var guard = prior as IfStatementSyntax;
            if (guard == null)
            {
                continue;
            }

            if (ConditionMeansDevelopment(guard.Condition, semanticModel, cancellationToken, expected: false) &&
                DefinitelyExits(guard.Statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConditionMeansDevelopment(
        ExpressionSyntax condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool expected)
    {
        var negated = false;
        while (condition is ParenthesizedExpressionSyntax parenthesized)
        {
            condition = parenthesized.Expression;
        }

        if (condition is PrefixUnaryExpressionSyntax prefix &&
            prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            negated = true;
            condition = prefix.Operand;
            while (condition is ParenthesizedExpressionSyntax inner)
            {
                condition = inner.Expression;
            }
        }

        var invocation = condition as InvocationExpressionSyntax;
        if (invocation == null)
        {
            return false;
        }

        var method = GetInvokedMethod(semanticModel, invocation, cancellationToken);
        if (method == null)
        {
            return false;
        }

        var candidate = method.ReducedFrom ?? method;
        var isDevelopment =
            candidate.Name == "IsDevelopment" &&
            (IsNamespaceOrChild(candidate, "Microsoft.Extensions.Hosting") ||
             IsNamespaceOrChild(candidate, "Microsoft.AspNetCore"));

        if (!isDevelopment && candidate.Name == "IsEnvironment")
        {
            var developmentArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (developmentArgument != null)
            {
                var value = semanticModel.GetConstantValue(developmentArgument.Expression, cancellationToken);
                isDevelopment = value.HasValue &&
                                string.Equals(value.Value as string, "Development", StringComparison.OrdinalIgnoreCase);
            }
        }

        return isDevelopment && (negated ? !expected : expected);
    }

    private static bool DefinitelyExits(StatementSyntax statement)
    {
        if (statement is ReturnStatementSyntax || statement is ThrowStatementSyntax)
        {
            return true;
        }

        var block = statement as BlockSyntax;
        if (block == null || block.Statements.Count == 0)
        {
            return false;
        }

        var last = block.Statements[block.Statements.Count - 1];
        return last is ReturnStatementSyntax || last is ThrowStatementSyntax;
    }

    internal static bool IsCancellableArgumentSupplied(IInvocationOperation operation)
    {
        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter != null && IsCancellationToken(argument.Parameter.Type))
            {
                if (argument.IsImplicit || argument.ArgumentKind == ArgumentKind.DefaultValue)
                {
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    internal static bool HasCancellationTokenParameter(IMethodSymbol method)
    {
        return method.Parameters.Any(parameter => IsCancellationToken(parameter.Type));
    }

    internal static bool HasApplicableCancellableOverload(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var tokenExpression = SyntaxFactory.DefaultExpression(
            SyntaxFactory.ParseTypeName(
                "global::System.Threading.CancellationToken"));
        var tokenParameterNames = semanticModel
            .GetMemberGroup(invocation.Expression, cancellationToken)
            .OfType<IMethodSymbol>()
            .SelectMany(candidate => candidate.Parameters)
            .Where(parameter => IsCancellationToken(parameter.Type))
            .Select(parameter => parameter.Name)
            .Distinct(StringComparer.Ordinal);
        foreach (var parameterName in tokenParameterNames)
        {
            var argument = SyntaxFactory.Argument(tokenExpression)
                .WithNameColon(
                    SyntaxFactory.NameColon(
                        SyntaxFactory.IdentifierName(parameterName)));
            var speculativeInvocation = invocation.WithArgumentList(
                invocation.ArgumentList.AddArguments(argument));
            var symbolInfo = semanticModel.GetSpeculativeSymbolInfo(
                invocation.SpanStart,
                speculativeInvocation,
                SpeculativeBindingOption.BindAsExpression);

            var boundParameter = (symbolInfo.Symbol as IMethodSymbol)?
                .Parameters
                .FirstOrDefault(parameter => parameter.Name == parameterName);
            if (boundParameter != null &&
                IsCancellationToken(boundParameter.Type))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsCancellationNone(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var memberAccess = expression as MemberAccessExpressionSyntax;
        if (memberAccess != null &&
            memberAccess.Name.Identifier.ValueText == "None")
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
            return symbol is IPropertySymbol property &&
                   property.Name == "None" &&
                   IsCancellationToken(property.ContainingType);
        }

        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            return IsCancellationToken(semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType);
        }

        var defaultExpression = expression as DefaultExpressionSyntax;
        return defaultExpression != null &&
               IsCancellationToken(semanticModel.GetTypeInfo(defaultExpression.Type, cancellationToken).Type);
    }

    internal static bool IsKnownAsyncTerminal(string name)
    {
        switch (name)
        {
            case "ToListAsync":
            case "ToArrayAsync":
            case "AnyAsync":
            case "AllAsync":
            case "CountAsync":
            case "LongCountAsync":
            case "ContainsAsync":
            case "FirstAsync":
            case "FirstOrDefaultAsync":
            case "SingleAsync":
            case "SingleOrDefaultAsync":
            case "LastAsync":
            case "LastOrDefaultAsync":
            case "MinAsync":
            case "MaxAsync":
            case "SumAsync":
            case "AverageAsync":
            case "ForEachAsync":
            case "LoadAsync":
            case "ExecuteDeleteAsync":
            case "ExecuteUpdateAsync":
                return true;
            default:
                return false;
        }
    }

    internal static bool IsEntityMaterializer(string name)
    {
        switch (name)
        {
            case "ToList":
            case "ToListAsync":
            case "ToArray":
            case "ToArrayAsync":
            case "First":
            case "FirstAsync":
            case "FirstOrDefault":
            case "FirstOrDefaultAsync":
            case "Single":
            case "SingleAsync":
            case "SingleOrDefault":
            case "SingleOrDefaultAsync":
            case "Last":
            case "LastAsync":
            case "LastOrDefault":
            case "LastOrDefaultAsync":
                return true;
            default:
                return false;
        }
    }

    internal static bool IsQueryableShapingMethod(string name)
    {
        switch (name)
        {
            case "Where":
            case "Select":
            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
            case "Skip":
            case "Take":
            case "GroupBy":
            case "Distinct":
                return true;
            default:
                return false;
        }
    }

    internal static bool ContainsBound(ExpressionSyntax expression)
    {
        return ContainsInvocationNamed(
            expression,
            "Take",
            "First",
            "FirstAsync",
            "FirstOrDefault",
            "FirstOrDefaultAsync",
            "Single",
            "SingleAsync",
            "SingleOrDefault",
            "SingleOrDefaultAsync");
    }

    internal static bool SymbolEquals(ISymbol? left, ISymbol? right)
    {
        return left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);
    }
}
