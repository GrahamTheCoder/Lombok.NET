﻿//HintName: Test_PropertyPersonWithValidationAttributes.Name.g.cs
// <auto-generated/>
using System.ComponentModel.DataAnnotations;
using Lombok.NET;

namespace Test;
#nullable enable
internal partial class PropertyPersonWithValidationAttributes
{
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string Name { get => _name; set => _name = value; }
}