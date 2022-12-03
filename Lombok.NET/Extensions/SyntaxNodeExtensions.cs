using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Lombok.NET.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Lombok.NET.Extensions
{
	internal static class SyntaxNodeExtensions
	{
		private static readonly IDictionary<AccessTypes, SyntaxKind> SyntaxKindsByAccessType = new Dictionary<AccessTypes, SyntaxKind>(4)
		{
			[AccessTypes.Private] = SyntaxKind.PrivateKeyword,
			[AccessTypes.Protected] = SyntaxKind.ProtectedKeyword,
			[AccessTypes.Internal] = SyntaxKind.InternalKeyword,
			[AccessTypes.Public] = SyntaxKind.PublicKeyword
		};

		/// <summary>
		/// Traverses a syntax node upwards until it reaches a <code>BaseNamespaceDeclarationSyntax</code>.
		/// </summary>
		/// <param name="node">The syntax node to traverse.</param>
		/// <returns>The namespace this syntax node is in. <code>null</code> if a namespace cannot be found.</returns>
		public static NameSyntax? GetNamespace(this SyntaxNode node)
		{
			var parent = node.Parent;
			while (parent != null)
			{
				if (parent is BaseNamespaceDeclarationSyntax ns)
				{
					return ns.Name;
				}

				parent = parent.Parent;
			}

			return null;
		}

		/// <summary>
		/// Gets the using directives from a SyntaxNode. Traverses the tree upwards until it finds using directives.
		/// </summary>
		/// <param name="node">The staring point.</param>
		/// <returns>A list of using directives.</returns>
		public static SyntaxList<UsingDirectiveSyntax> GetUsings(this SyntaxNode node)
		{
			var parent = node.Parent;
			while (parent is not null)
			{
				if (parent is BaseNamespaceDeclarationSyntax ns && ns.Usings.Any())
				{
					return ns.Usings;
				}

				if (parent is CompilationUnitSyntax compilationUnit && compilationUnit.Usings.Any())
				{
					return compilationUnit.Usings;
				}

				parent = parent.Parent;
			}

			return default;
		}

		/// <summary>
		/// Gets the accessibility modifier for a type declaration.
		/// </summary>
		/// <param name="typeDeclaration">The type declaration's accessibility modifier to find.</param>
		/// <returns>The types accessibility modifier.</returns>
		public static SyntaxKind GetAccessibilityModifier(this BaseTypeDeclarationSyntax typeDeclaration)
		{
			if (typeDeclaration.Modifiers.Any(SyntaxKind.PublicKeyword))
			{
				return SyntaxKind.PublicKeyword;
			}

			return SyntaxKind.InternalKeyword;
		}

		/// <summary>
		/// Constructs a new partial type from the original type's name, accessibility and type arguments.
		/// </summary>
		/// <param name="typeDeclaration">The type to clone.</param>
		/// <returns>A new partial type with a few of the original types traits.</returns>
		public static TypeDeclarationSyntax CreateNewPartialType(this TypeDeclarationSyntax typeDeclaration)
		{
			if (typeDeclaration.IsKind(SyntaxKind.ClassDeclaration))
			{
				return typeDeclaration.CreateNewPartialClass();
			}
			
			if (typeDeclaration.IsKind(SyntaxKind.StructDeclaration))
			{
				return typeDeclaration.CreateNewPartialStruct();
			}

			if (typeDeclaration.IsKind(SyntaxKind.InterfaceDeclaration))
			{
				return typeDeclaration.CreateNewPartialInterface();
			}

			return typeDeclaration;
		}

		/// <summary>
		/// Constructs a new partial class from the original type's name, accessibility and type arguments.
		/// </summary>
		/// <param name="type">The type to clone.</param>
		/// <returns>A new partial class with a few of the original types traits.</returns>
		public static ClassDeclarationSyntax CreateNewPartialClass(this TypeDeclarationSyntax type)
		{
			return ClassDeclaration(type.Identifier.Text)
				.WithModifiers(
					TokenList(
						Token(type.GetAccessibilityModifier()),
						Token(SyntaxKind.PartialKeyword)
					)
				).WithTypeParameterList(type.TypeParameterList);
		}

		/// <summary>
		/// Constructs a new partial struct from the original type's name, accessibility and type arguments.
		/// </summary>
		/// <param name="type">The type to clone.</param>
		/// <returns>A new partial struct with a few of the original types traits.</returns>
		public static StructDeclarationSyntax CreateNewPartialStruct(this TypeDeclarationSyntax type)
		{
			return StructDeclaration(type.Identifier.Text)
				.WithModifiers(
					TokenList(
						Token(type.GetAccessibilityModifier()),
						Token(SyntaxKind.PartialKeyword)
					)
				).WithTypeParameterList(type.TypeParameterList);
		}

		/// <summary>
		/// Constructs a new partial interface from the original type's name, accessibility and type arguments.
		/// </summary>
		/// <param name="type">The type to clone.</param>
		/// <returns>A new partial interface with a few of the original types traits.</returns>
		public static InterfaceDeclarationSyntax CreateNewPartialInterface(this TypeDeclarationSyntax type)
		{
			return InterfaceDeclaration(type.Identifier.Text)
				.WithModifiers(
					TokenList(
						Token(type.GetAccessibilityModifier()),
						Token(SyntaxKind.PartialKeyword)
					)
				).WithTypeParameterList(type.TypeParameterList);
		}

		/// <summary>
		/// Checks if a TypeSyntax represents void.
		/// </summary>
		/// <param name="typeSyntax">The TypeSyntax to check.</param>
		/// <returns>True, if the type represents void.</returns>
		public static bool IsVoid(this TypeSyntax typeSyntax)
		{
			return typeSyntax is PredefinedTypeSyntax predefinedType && predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword);
		}

		/// <summary>
		/// Checks if a type is declared as a nested type.
		/// </summary>
		/// <param name="typeDeclaration">The type to check.</param>
		/// <returns>True, if the type is declared within another type.</returns>
		public static bool IsNestedType(this TypeDeclarationSyntax typeDeclaration)
		{
			return typeDeclaration.Parent is TypeDeclarationSyntax;
		}

		/// <summary>
		/// Determines if the type is eligible for code generation.
		/// </summary>
		/// <param name="typeDeclaration">The type to check for.</param>
		/// <param name="namespace">The type's namespace. Will be set in this method.</param>
		/// <param name="diagnostic">A diagnostic to be emitted if the type is not valid.</param>
		/// <returns>True, if code can be generated for this type.</returns>
		public static bool TryValidateType(this TypeDeclarationSyntax typeDeclaration, [NotNullWhen(true)] out NameSyntax? @namespace, [NotNullWhen(false)] out Diagnostic? diagnostic)
		{
			@namespace = null;
			diagnostic = null;
			if (!typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
			{
				diagnostic = Diagnostic.Create(DiagnosticDescriptors.TypeMustBePartial, typeDeclaration.Identifier.GetLocation(), typeDeclaration.Identifier.Text);

				return false;
			}

			if (typeDeclaration.IsNestedType())
			{
				diagnostic = Diagnostic.Create(DiagnosticDescriptors.TypeMustBeNonNested, typeDeclaration.Identifier.GetLocation(), typeDeclaration.Identifier.Text);

				return false;
			}
			
			@namespace = typeDeclaration.GetNamespace();
			if (@namespace is null)
			{
				diagnostic = Diagnostic.Create(DiagnosticDescriptors.TypeMustHaveNamespace, typeDeclaration.Identifier.GetLocation(), typeDeclaration.Identifier.Text);

				return false;
			}

			return true;
		}

		/// <summary>
		/// Creates a using directive from a string.
		/// </summary>
		/// <param name="usingQualifier">The name of the using directive.</param>
		/// <returns>A using directive.</returns>
		public static UsingDirectiveSyntax CreateUsingDirective(this string usingQualifier)
		{
			var usingParts = usingQualifier.Split('.');

			static NameSyntax GetParts(string[] parts)
			{
				if (parts.Length == 1)
				{
					return IdentifierName(parts[0]);
				}

				var newParts = new string[parts.Length - 1];
				Array.Copy(parts, newParts, newParts.Length);

				return QualifiedName(
					GetParts(newParts),
					IdentifierName(parts[parts.Length - 1])
				);
			}

			return UsingDirective(
				GetParts(usingParts)
			);
		}

		/// <summary>
		/// Removes all the members which do not have the desired access modifier.
		/// </summary>
		/// <param name="members">The members to filter</param>
		/// <param name="accessType">The access modifer to look out for.</param>
		/// <typeparam name="T">The type of the members (<code>PropertyDeclarationSyntax</code>/<code>FieldDeclarationSyntax</code>).</typeparam>
		/// <returns>The members which have the desired access modifier.</returns>
		/// <exception cref="ArgumentOutOfRangeException">If an access modifier is supplied which is not supported.</exception>
		public static IEnumerable<T> Where<T>(this IEnumerable<T> members, AccessTypes accessType)
			where T : MemberDeclarationSyntax
		{
			var predicateBuilder = PredicateBuilder.False<T>();
			foreach (AccessTypes t in typeof(AccessTypes).GetEnumValues())
			{
				if (accessType.HasFlag(t))
				{
					predicateBuilder = predicateBuilder.Or(m => m.Modifiers.Any(SyntaxKindsByAccessType[t]));
				}
			}

			return members.Where(predicateBuilder.Compile());
		}
	}
}

namespace System.Diagnostics.CodeAnalysis
{
	/// <summary>Specifies that when a method returns <see cref="ReturnValue"/>, the parameter will not be null even if the corresponding type allows it.</summary>
	[AttributeUsage(AttributeTargets.Parameter)]
	internal sealed class NotNullWhenAttribute : Attribute
	{
		/// <summary>Gets the return value condition.</summary>
		public bool ReturnValue { get; }
		
		/// <summary>Initializes the attribute with the specified return value condition.</summary>
		/// <param name="returnValue">
		/// The return value condition. If the method returns this value, the associated parameter will not be null.
		/// </param>
		public NotNullWhenAttribute(bool returnValue)
		{
			ReturnValue = returnValue;
		}
	}
	
	/// <summary>Specifies that the method or property will ensure that the listed field and property members have not-null values when returning with the specified return value condition.</summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
	internal sealed class MemberNotNullWhenAttribute : Attribute
	{
		/// <summary>Initializes the attribute with the specified return value condition and a field or property member.</summary>
		/// <param name="returnValue">
		/// The return value condition. If the method returns this value, the associated parameter will not be null.
		/// </param>
		/// <param name="member">
		/// The field or property member that is promised to be not-null.
		/// </param>
		public MemberNotNullWhenAttribute(bool returnValue, string member)
		{
			ReturnValue = returnValue;
			Members = new[] { member };
		}

		/// <summary>Initializes the attribute with the specified return value condition and list of field and property members.</summary>
		/// <param name="returnValue">
		/// The return value condition. If the method returns this value, the associated parameter will not be null.
		/// </param>
		/// <param name="members">
		/// The list of field and property members that are promised to be not-null.
		/// </param>
		public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
		{
			ReturnValue = returnValue;
			Members = members;
		}

		/// <summary>Gets the return value condition.</summary>
		public bool ReturnValue { get; }

		/// <summary>Gets field or property member names.</summary>
		public string[] Members { get; }
	}
}
