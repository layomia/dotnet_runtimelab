﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace System.Text.Json.SourceGeneration
{
    internal sealed partial class JsonSourceGeneratorHelper
    {
        // Simple handled types with typeinfo.
        private readonly HashSet<Type> _simpleTypes = new();

        private Type _stringType;

        // Generation namespace for source generation code.
        const string GenerationNamespace = "JsonCodeGeneration";

        // TypeWrapper for key and <TypeInfoIdentifier, Source> for value.
        public Dictionary<Type, Tuple<string, string>> Types { get; }

        // Contains types that failed to be generated.
        private HashSet<Type> _failedTypes = new HashSet<Type>();

        // Contains used typeinfo identifiers.
        private HashSet<string> _usedTypeInfoIdentifiers = new HashSet<string>();

        // Contains list of diagnostics for the code generator.
        public List<Diagnostic> Diagnostics { get; }

        public JsonSourceGeneratorHelper(MetadataLoadContext metadataLoadContext)
        {
            // Initialize auto properties.
            Types = new Dictionary<Type, Tuple<string, string>>();
            Diagnostics = new List<Diagnostic>();

            PopulateSimpleTypes(metadataLoadContext);

            // Initiate diagnostic descriptors.
            InitializeDiagnosticDescriptors();
        }

        private void PopulateSimpleTypes(MetadataLoadContext metadataLoadContext)
        {
            Debug.Assert(_simpleTypes != null);

            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(bool)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(byte[])));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(byte)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(char)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(DateTime)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(DateTimeOffset)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(Decimal)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(double)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(Guid)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(short)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(int)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(long)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(sbyte)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(float)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(ushort)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(uint)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(ulong)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(Uri)));
            _simpleTypes.Add(metadataLoadContext.Resolve(typeof(Version)));

            _stringType = metadataLoadContext.Resolve(typeof(string));
            _simpleTypes.Add(_stringType);
        }

        public class GenerationClassFrame
        {
            public Type RootType;
            public Type CurrentType;
            public string ClassName;
            public StringBuilder Source;

            public PropertyInfo[] Properties;
            public FieldInfo[] Fields;

            public bool IsSuccessful; 

            public GenerationClassFrame(Type rootType, Type currentType, HashSet<string> usedTypeInfoIdentifiers)
            {
                RootType = rootType;
                CurrentType = currentType;
                Source = new StringBuilder();
                Properties = CurrentType.GetProperties();
                Fields = CurrentType.GetFields();
                IsSuccessful = true;

                // If typename was already used, use unique name instead.
                ClassName = usedTypeInfoIdentifiers.Contains(currentType.Name) ?
                    currentType.GetCompilableUniqueName() : currentType.Name;

                // Register new ClassName.
                usedTypeInfoIdentifiers.Add(ClassName);
            }
        }

        // Base source generation context partial class.
        public string GenerateHelperContextInfo()
        {
            return @$"
using System.Text.Json;
using System.Text.Json.Serialization;

namespace {GenerationNamespace}
{{
    public partial class JsonContext : JsonSerializerContext
    {{
        private static JsonContext s_instance;
        public static JsonContext Instance
        {{
            get
            {{
                if (s_instance == null)
                {{
                    s_instance = new JsonContext();
                }}

                return s_instance;
            }}
        }}

        public JsonContext()
        {{
        }}

        public JsonContext(JsonSerializerOptions options) : base(options)
        {{
        }}
    }}
}}
            ";
        }

        // Generates metadata for type and returns if it was successful.
        private bool GenerateClassInfo(GenerationClassFrame currentFrame, Dictionary<Type, string> seenTypes)
        {
            // Add current type to seen types along with its className..
            seenTypes[currentFrame.CurrentType] = currentFrame.ClassName;

            // Try to recursively generate necessary field and property types.
            foreach (FieldInfo field in currentFrame.Fields)
            {
                if (!IsSupportedType(field.FieldType))
                {
                    Diagnostics.Add(Diagnostic.Create(_notSupported, Location.None, new string[] { currentFrame.RootType.Name, field.FieldType.Name }));
                    return false;
                }
                foreach (Type handlingType in GetTypesToGenerate(field.FieldType))
                {
                    GenerateForMembers(currentFrame, handlingType, seenTypes);
                }
            }

            foreach (PropertyInfo property in currentFrame.Properties)
            {
                if (!IsSupportedType(property.PropertyType))
                {
                    Diagnostics.Add(Diagnostic.Create(_notSupported, Location.None, new string[] { currentFrame.RootType.Name, property.PropertyType.Name }));
                    return false;
                }
                foreach (Type handlingType in GetTypesToGenerate(property.PropertyType))
                {
                    GenerateForMembers(currentFrame, handlingType, seenTypes);
                }
            }

            // Try to generate current type info now that fields and property types have been resolved.
            AddImportsToTypeClass(currentFrame);
            InitializeContextClass(currentFrame);
            InitializeTypeClass(currentFrame);
            TypeInfoGetterSetter(currentFrame);
            currentFrame.IsSuccessful &= InitializeTypeInfoProperties(currentFrame);
            currentFrame.IsSuccessful &= GenerateTypeInfoConstructor(currentFrame, seenTypes);
            GenerateCreateObject(currentFrame);
            GenerateSerialize(currentFrame);
            GenerateDeserialize(currentFrame);
            FinalizeTypeAndContextClasses(currentFrame);

            if (currentFrame.IsSuccessful)
            {
                Diagnostics.Add(Diagnostic.Create(_generatedTypeClass, Location.None, new string[] { currentFrame.RootType.Name, currentFrame.ClassName }));

                // Add generated typeinfo for current traversal.
                Types.Add(currentFrame.CurrentType, new Tuple<string, string>(currentFrame.ClassName, currentFrame.Source.ToString()));
                // If added type had its typeinfo name changed, report to the user.
                if (currentFrame.CurrentType.Name != currentFrame.ClassName)
                {
                    Diagnostics.Add(Diagnostic.Create(_typeNameClash, Location.None, new string[] { currentFrame.CurrentType.Name, currentFrame.ClassName }));
                }
            }
            else
            {
                Diagnostics.Add(Diagnostic.Create(_failedToGenerateTypeClass, Location.None, new string[] { currentFrame.RootType.Name, currentFrame.ClassName }));

                // If not successful remove it from found types hashset and add to failed types list.
                seenTypes.Remove(currentFrame.CurrentType);
                _failedTypes.Add(currentFrame.CurrentType);

                // Unregister typeinfo identifier since typeinfo will not be saved.
                _usedTypeInfoIdentifiers.Remove(currentFrame.ClassName);
            }

            return currentFrame.IsSuccessful;
        }

        // Call recursive type generation if unseen type and check for success and cycles.
        void GenerateForMembers(GenerationClassFrame currentFrame, Type newType, Dictionary<Type, string> seenTypes)
        {
            // If new type, recurse.
            if (IsNewType(newType, seenTypes))
            {
                bool isMemberSuccessful = GenerateClassInfo(new GenerationClassFrame(currentFrame.RootType, newType, _usedTypeInfoIdentifiers), seenTypes);
                currentFrame.IsSuccessful &= isMemberSuccessful;

                if (!isMemberSuccessful)
                {
                    Diagnostics.Add(Diagnostic.Create(_failedToAddNewTypesFromMembers, Location.None, new string[] { currentFrame.RootType.Name, currentFrame.CurrentType.Name }));
                }
            }
        }

        // Check if current type is supported to be iterated over.
        private bool IsSupportedType(Type type)
        {
            if (type.IsArray)
            {
                return true;
            }
            if (type.IsIEnumerable() && type != _stringType)
            {
                // todo: Add more support to collections.
                if (!type.IsIList() && !type.IsIDictionary())
                {
                    return false;
                }
            }

            return true;
        }

        // Returns name of types traversed that can be looked up in the dictionary.
        public void GenerateClassInfo(Type type)
        {
            Dictionary<Type, string> foundTypes = new Dictionary<Type, string>();
            GenerateClassInfo(new GenerationClassFrame(rootType: type, currentType: type, _usedTypeInfoIdentifiers), foundTypes);
        }

        private Type[] GetTypesToGenerate(Type type)
        {
            if (type.IsArray)
            {
                return new Type[] { type.GetElementType() };
            }
            if (type.IsGenericType)
            {
                return type.GetGenericArguments();
            }

            return new Type[] { type };
        }

        private bool IsNewType(Type type, Dictionary<Type, string> foundTypes) => (
            !Types.ContainsKey(type) &&
            !foundTypes.ContainsKey(type) &&
            !_simpleTypes.Contains(type));

        private string GetTypeInfoIdentifier(Type type, Dictionary<Type, string> seenTypes)
        {
            if (_simpleTypes.Contains(type))
            {
                return type.Name;
            }
            if (Types.ContainsKey(type))
            {
                return Types[type].Item1;
            }

            return seenTypes[type];
        }

        private void AddImportsToTypeClass(GenerationClassFrame currentFrame)
        {
            HashSet<string> imports = new HashSet<string>();

            // Add base imports.
            imports.Add("System");
            imports.Add("System.Collections");
            imports.Add("System.Collections.Generic");
            imports.Add("System.Text.Json");
            imports.Add("System.Text.Json.Serialization");
            imports.Add("System.Text.Json.Serialization.Metadata");

            // Add imports to root type.
            imports.Add(currentFrame.CurrentType.GetFullNamespace());

            foreach (PropertyInfo property in currentFrame.Properties)
            {
                foreach (Type handlingType in GetTypesToGenerate(property.PropertyType))
                {
                    imports.Add(property.PropertyType.GetFullNamespace());
                    imports.Add(handlingType.GetFullNamespace());
                }
            }
            foreach (FieldInfo field in currentFrame.Fields)
            {
                foreach (Type handlingType in GetTypesToGenerate(field.FieldType))
                {
                    imports.Add(field.FieldType.GetFullNamespace());
                    imports.Add(handlingType.GetFullNamespace());
                }
            }

            foreach (string import in imports)
            {
                if (import.Length > 0)
                {
                    currentFrame.Source.Append($@"
using {import};");
                }
            }
        }

        // Includes necessary imports, namespace decl and initializes class.
        private void InitializeContextClass(GenerationClassFrame currentFrame)
        {
            currentFrame.Source.Append($@"

namespace {GenerationNamespace}
{{
    public partial class JsonContext : JsonSerializerContext
    {{
        private {currentFrame.ClassName}TypeInfo _{currentFrame.ClassName};
        public JsonTypeInfo<{currentFrame.CurrentType.FullName}> {currentFrame.ClassName}
        {{
            get
            {{
                if (_{currentFrame.ClassName} == null)
                {{
                    _{currentFrame.ClassName} = new {currentFrame.ClassName}TypeInfo(this);
                }}

                return _{currentFrame.ClassName}.TypeInfo;
            }}
        }}
        ");
        }

        private void InitializeTypeClass(GenerationClassFrame currentFrame) {
            currentFrame.Source.Append($@"
        private class {currentFrame.ClassName}TypeInfo 
        {{
        ");
        }

        private void TypeInfoGetterSetter(GenerationClassFrame currentFrame)
        {
            currentFrame.Source.Append($@"
            public JsonTypeInfo<{currentFrame.CurrentType.FullName}> TypeInfo {{ get; private set; }}
            ");
        }

        private bool InitializeTypeInfoProperties(GenerationClassFrame currentFrame)
        {
            Type propertyType;
            Type[] genericTypes;
            string typeName;
            string propertyName;

            foreach (PropertyInfo property in currentFrame.Properties)
            {
                // Find type and property name to use for property definition.
                propertyType = property.PropertyType;
                propertyName = property.Name;
                typeName = propertyType.FullName;

                genericTypes = GetTypesToGenerate(propertyType);

                // Check if Array.
                if (propertyType.IsArray)
                {
                    typeName = $"{genericTypes[0].FullName}[]";
                }

                // Check if IEnumerable.
                if (propertyType.IsIEnumerable())
                {
                    if (propertyType.IsIList())
                    {
                        typeName = $"List<{genericTypes[0].FullName}>";
                    }
                    else if (propertyType.IsIDictionary())
                    {
                        typeName = $"Dictionary<{genericTypes[0].FullName}, {genericTypes[1].FullName}>";
                    }
                    else
                    {
                        // todo: Add support for rest of the IEnumerables.
                        return false;
                    }
                }

                currentFrame.Source.Append($@"
            private JsonPropertyInfo<{typeName}> _property_{propertyName};
                ");
            }

            return true;
        }

        private bool GenerateTypeInfoConstructor(GenerationClassFrame currentFrame, Dictionary<Type, string> seenTypes)
        {
            Type currentType = currentFrame.CurrentType;

            currentFrame.Source.Append($@"
            public {currentFrame.ClassName}TypeInfo(JsonContext context)
            {{
                var typeInfo = new JsonObjectInfo<{currentType.FullName}>(CreateObjectFunc, SerializeFunc, DeserializeFunc, context.GetOptions());
            ");


            Type[] genericTypes;
            Type propertyType;
            string typeClassInfoCall;
            string typeInfoIdentifier;

            foreach (PropertyInfo property in currentFrame.Properties)
            {
                propertyType = property.PropertyType;
                genericTypes = GetTypesToGenerate(propertyType);

                if (propertyType.IsArray)
                {
                    typeInfoIdentifier = GetTypeInfoIdentifier(genericTypes[0], seenTypes);
                    typeClassInfoCall = $"KnownCollectionTypeInfos<{genericTypes[0].FullName}>.GetArray(context.{typeInfoIdentifier}, context)";
                }
                else if (propertyType.IsIEnumerable())
                {
                    if (propertyType.IsIList())
                    {
                        typeInfoIdentifier = GetTypeInfoIdentifier(genericTypes[0], seenTypes);
                        typeClassInfoCall = $"KnownCollectionTypeInfos<{genericTypes[0].FullName}>.GetList(context.{typeInfoIdentifier}, context)";
                    }
                    else if (propertyType.IsIDictionary())
                    {
                        typeInfoIdentifier = GetTypeInfoIdentifier(genericTypes[1], seenTypes);
                        typeClassInfoCall = $"KnownDictionaryTypeInfos<{genericTypes[0].FullName}, {genericTypes[1].FullName}>.GetDictionary(context.{typeInfoIdentifier}, context)";
                    }
                    else
                    {
                        // todo: Add support for rest of the IEnumerables.
                        return false;
                    }
                }
                else
                {
                    // Default classtype for values.
                    typeInfoIdentifier = GetTypeInfoIdentifier(propertyType, seenTypes);
                    typeClassInfoCall = $"context.{typeInfoIdentifier}";
                }


                currentFrame.Source.Append($@"
                _property_{property.Name} = typeInfo.AddProperty(nameof({currentType.FullName}.{property.Name}),
                    (obj) => {{ return (({currentType.FullName})obj).{property.Name}; }},
                    (obj, value) => {{ (({currentType.FullName})obj).{property.Name} = value; }},
                    {typeClassInfoCall});
                ");
            }

            // Finalize constructor.
            currentFrame.Source.Append($@"
                typeInfo.CompleteInitialization();
                TypeInfo = typeInfo;
            }}
            ");

            return true;
        }

        private void GenerateCreateObject(GenerationClassFrame currentFrame)
        {
            currentFrame.Source.Append($@"
            private object CreateObjectFunc()
            {{
                return new {currentFrame.CurrentType.FullName}();
            }}
            ");
        }

        private void GenerateSerialize(GenerationClassFrame currentFrame)
        {
            // Start function.
            currentFrame.Source.Append($@"
            private void SerializeFunc(Utf8JsonWriter writer, object value, ref WriteStack writeStack, JsonSerializerOptions options)
            {{");

            // Create base object.
            currentFrame.Source.Append($@"
                {currentFrame.CurrentType.FullName} obj = ({currentFrame.CurrentType.FullName})value;
            ");

            foreach (PropertyInfo property in currentFrame.Properties)
            {
                currentFrame.Source.Append($@"
                _property_{property.Name}.WriteValue(obj.{property.Name}, ref writeStack, writer);");
            }

            // End function.
            currentFrame.Source.Append($@"
            }}
            ");
        }

        private void GenerateDeserialize(GenerationClassFrame currentFrame)
        {
            // Start deserialize function.
            currentFrame.Source.Append($@"
            private {currentFrame.CurrentType.FullName} DeserializeFunc(ref Utf8JsonReader reader, ref ReadStack readStack, JsonSerializerOptions options)
            {{
            ");

            // Create helper function to check for property name.
            currentFrame.Source.Append($@"
                bool ReadPropertyName(ref Utf8JsonReader reader)
                {{
                    return reader.Read() && reader.TokenType == JsonTokenType.PropertyName;
                }}
            ");

            // Start loop to read properties.
            currentFrame.Source.Append($@"
                ReadOnlySpan<byte> propertyName;
                {currentFrame.CurrentType.FullName} obj = new {currentFrame.CurrentType.FullName}();

                while(ReadPropertyName(ref reader))
                {{
                    propertyName = reader.ValueSpan;
            ");

            // Read and set each property.
            foreach ((PropertyInfo property, int i) in currentFrame.Properties.Select((p, i) => (p, i)))
            {
                currentFrame.Source.Append($@"
                    {((i == 0) ? "" : "else ")}if (propertyName.SequenceEqual(_property_{property.Name}.NameAsUtf8Bytes))
                    {{
                        reader.Read();
                        _property_{property.Name}.ReadValueAndSetMember(ref reader, ref readStack, obj);
                    }}");
            }

            // Base condition for unhandled properties.
            if (currentFrame.Properties.Length > 0)
            {
                currentFrame.Source.Append($@"
                    else
                    {{
                        reader.Read();
                    }}");
            }
            else
            {
                currentFrame.Source.Append($@"
                    reader.Read();");
            }

            // Finish property reading loops.
            currentFrame.Source.Append($@"
                }}
            ");

            // Verify the final received token and return object.
            currentFrame.Source.Append($@"
                if (reader.TokenType != JsonTokenType.EndObject)
                {{
                    throw new JsonException(""todo"");
                }}
                return obj;
            ");

            // End deserialize function.
            currentFrame.Source.Append($@"
            }}
            ");
        }

        private void FinalizeTypeAndContextClasses(GenerationClassFrame currentFrame)
        {
            currentFrame.Source.Append($@"
        }} // End of typeinfo class.
    }} // End of context class.
}} // End of namespace.
            ");
        }
    }
}
