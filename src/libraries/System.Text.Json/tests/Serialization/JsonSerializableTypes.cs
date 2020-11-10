// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

using TestNamespace = System.Text.Json.Serialization.Tests;

//[module: JsonSerializable(typeof(TestNamespace.SimpleTestClass))]
[module: JsonSerializable(typeof(bool))]
