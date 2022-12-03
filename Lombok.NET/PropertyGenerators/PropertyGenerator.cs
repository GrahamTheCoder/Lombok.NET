using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Lombok.NET.Analyzers;
using Lombok.NET.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

#if DEBUG
using System.Diagnostics;
#endif

namespace Lombok.NET.PropertyGenerators;

/// <summary>
/// Generator which generates properties from fields.
/// </summary>
[Generator]
public sealed class PropertyGenerator : IIncrementalGenerator
{
	private static readonly string AttributeName = typeof(PropertyAttribute).FullName;

	/// <summary>
	/// Initializes the generator logic.
	/// </summary>
	/// <param name="context">The context of initializing the generator.</param>
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUG
		SpinWait.SpinUntil(static () => Debugger.IsAttached);
#endif
		var sources = context.SyntaxProvider.ForAttributeWithMetadataName(AttributeName, IsCandidate, Transform);
		context.AddSources(sources);
	}

	private static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
	{
		return node is VariableDeclaratorSyntax;
	}

	private static GeneratorResult Transform(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
	{
		var declarator = (VariableDeclaratorSyntax)context.TargetNode;
		var field = (IFieldSymbol)context.TargetSymbol;

		var propertyChangeTypeArgument = context.Attributes[0].NamedArguments.FirstOrDefault(kv => kv.Key == nameof(PropertyAttribute.PropertyChangeType));
		var propertyChangeType = (PropertyChangeType?)(propertyChangeTypeArgument.Value.Value as int?);

		cancellationToken.ThrowIfCancellationRequested();

		var property = field.IsReadOnly
			? CreateReadonlyProperty(IdentifierName(field.Type.ToString()), field.Name)
			: CreateProperty(IdentifierName(field.Type.ToString()), field.Name, propertyChangeType);

		Diagnostic? diagnostic = null;
		// VariableDeclarator -> VariableDeclaration -> FieldDeclaration -> TypeDeclaration
		var type = (TypeDeclarationSyntax)declarator.Parent!.Parent!.Parent!;
		if (type is ClassDeclarationSyntax or StructDeclarationSyntax && type.TryValidateType(out var @namespace, out diagnostic))
		{
			var sourceText = CreateTypeWithProperty(@namespace, type, property);

			return new GeneratorResult($"{type.Identifier.Text}.{property.Identifier.Text}", sourceText);
		}

		diagnostic ??= Diagnostic.Create(DiagnosticDescriptors.PropertyFieldMustBeInClassOrStruct, declarator.GetLocation());

		return new GeneratorResult(diagnostic);
	}

	private static PropertyDeclarationSyntax CreateReadonlyProperty(TypeSyntax type, string fieldName)
	{
		return PropertyDeclaration(type, fieldName.ToPascalCaseIdentifier())
			.WithModifiers(
				TokenList(
					Token(SyntaxKind.PublicKeyword)
				)
			).WithExpressionBody(
				ArrowExpressionClause(
					IdentifierName(fieldName)
				)
			).WithSemicolonToken(
				Token(SyntaxKind.SemicolonToken)
			);
	}

	private static PropertyDeclarationSyntax CreateProperty(TypeSyntax type, string fieldName, PropertyChangeType? propertyChangeType)
	{
		return PropertyDeclaration(type, fieldName.ToPascalCaseIdentifier())
			.WithModifiers(
				TokenList(
					Token(SyntaxKind.PublicKeyword)
				)
			).WithAccessorList(
				AccessorList(
					List(
						new[]
						{
							AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
								.WithExpressionBody(
									ArrowExpressionClause(
										IdentifierName(fieldName)
									)
								).WithSemicolonToken(
									Token(SyntaxKind.SemicolonToken)
								),
							AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
								.WithExpressionBody(
									CreatePropertySetter(fieldName, propertyChangeType)
								).WithSemicolonToken(
									Token(SyntaxKind.SemicolonToken)
								)
						}
					)
				)
			);
	}

	private static ArrowExpressionClauseSyntax CreatePropertySetter(string fieldName, PropertyChangeType? propertyChangeType)
	{
		switch (propertyChangeType)
		{
			case PropertyChangeType.PropertyChanged:
				return ArrowExpressionClause(
					InvocationExpression(
						IdentifierName(NotifyPropertyChangedGenerator.SetFieldMethodName)
					).WithArgumentList(
						ArgumentList(
							SeparatedList<ArgumentSyntax>(
								new SyntaxNodeOrToken[]
								{
									Argument(
										IdentifierName(fieldName)
									).WithRefOrOutKeyword(
										Token(SyntaxKind.OutKeyword)
									),
									Token(SyntaxKind.CommaToken),
									Argument(
										IdentifierName("value")
									)
								}
							)
						)
					)
				);
			case PropertyChangeType.PropertyChanging:
				return ArrowExpressionClause(
					InvocationExpression(
						IdentifierName(NotifyPropertyChangingGenerator.SetFieldMethodName)
					).WithArgumentList(
						ArgumentList(
							SeparatedList<ArgumentSyntax>(
								new SyntaxNodeOrToken[]
								{
									Argument(
										IdentifierName(fieldName)
									).WithRefOrOutKeyword(
										Token(SyntaxKind.OutKeyword)
									),
									Token(SyntaxKind.CommaToken),
									Argument(
										IdentifierName("value")
									)
								}
							)
						)
					)
				);
			case PropertyChangeType.ReactivePropertyChange:
				return ArrowExpressionClause(
					InvocationExpression(
						MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								AliasQualifiedName(
									IdentifierName(
										Token(SyntaxKind.GlobalKeyword)
									),
									IdentifierName("ReactiveUI")
								),
								IdentifierName("IReactiveObjectExtensions")
							),
							IdentifierName("RaiseAndSetIfChanged")
						)
					).WithArgumentList(
						ArgumentList(
							SeparatedList<ArgumentSyntax>(
								new SyntaxNodeOrToken[]
								{
									Argument(
										ThisExpression()
									),
									Token(SyntaxKind.CommaToken),
									Argument(
										IdentifierName(fieldName)
									).WithRefOrOutKeyword(
										Token(SyntaxKind.RefKeyword)
									),
									Token(SyntaxKind.CommaToken),
									Argument(IdentifierName("value"))
								}
							)
						)
					)
				);
			default:
				return ArrowExpressionClause(
					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName(fieldName),
						IdentifierName("value")
					)
				);
		}
	}

	private static SourceText CreateTypeWithProperty(NameSyntax @namespace,
		TypeDeclarationSyntax typeDeclaration,
		PropertyDeclarationSyntax property)
	{
		return CompilationUnit()
			.WithUsings(typeDeclaration.GetUsings())
			.WithMembers(
				SingletonList<MemberDeclarationSyntax>(
					FileScopedNamespaceDeclaration(@namespace)
						.WithMembers(
							SingletonList<MemberDeclarationSyntax>(
								typeDeclaration.CreateNewPartialType()
									.WithMembers(
										SingletonList<MemberDeclarationSyntax>(property)
									)
							)
						)
				)
			).NormalizeWhitespace()
			.GetText(Encoding.UTF8);
	}
}