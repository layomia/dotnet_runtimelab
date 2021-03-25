// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Xunit;

namespace System.Text.Json.Serialization.Tests
{
    public abstract partial class CustomConverterTests
    {
        // A polymorphic POCO converter using a type discriminator.
        private class PersonConverterWithTypeDiscriminator : JsonConverter<Person>
        {
            enum TypeDiscriminator
            {
                Customer = 1,
                Employee = 2
            }

            public override bool CanConvert(Type typeToConvert)
            {
                return typeof(Person).IsAssignableFrom(typeToConvert);
            }

            public override Person Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException();
                }

                reader.Read();
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException();
                }

                string propertyName = reader.GetString();
                if (propertyName != "TypeDiscriminator")
                {
                    throw new JsonException();
                }

                reader.Read();
                if (reader.TokenType != JsonTokenType.Number)
                {
                    throw new JsonException();
                }

                Person value;
                TypeDiscriminator typeDiscriminator = (TypeDiscriminator)reader.GetInt32();
                switch (typeDiscriminator)
                {
                    case TypeDiscriminator.Customer:
                        value = new Customer();
                        break;

                    case TypeDiscriminator.Employee:
                        value = new Employee();
                        break;

                    default:
                        throw new JsonException();
                }

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return value;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        propertyName = reader.GetString();
                        reader.Read();
                        switch (propertyName)
                        {
                            case "CreditLimit":
                                decimal creditLimit = reader.GetDecimal();
                                ((Customer)value).CreditLimit = creditLimit;
                                break;
                            case "OfficeNumber":
                                string officeNumber = reader.GetString();
                                ((Employee)value).OfficeNumber = officeNumber;
                                break;
                            case "Name":
                                string name = reader.GetString();
                                value.Name = name;
                                break;
                        }
                    }
                }

                throw new JsonException();
            }

            public override void Write(Utf8JsonWriter writer, Person value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();

                if (value is Customer)
                {
                    writer.WriteNumber("TypeDiscriminator", (int)TypeDiscriminator.Customer);
                    writer.WriteNumber("CreditLimit", ((Customer)value).CreditLimit);
                }
                else if (value is Employee)
                {
                    writer.WriteNumber("TypeDiscriminator", (int)TypeDiscriminator.Employee);
                    writer.WriteString("OfficeNumber", ((Employee)value).OfficeNumber);
                }

                writer.WriteString("Name", value.Name);

                writer.WriteEndObject();
            }
        }

        [Fact]
        public async Task PersonConverterPolymorphicTypeDiscriminator()
        {
            const string customerJson = @"{""TypeDiscriminator"":1,""CreditLimit"":100.00,""Name"":""C""}";
            const string employeeJson = @"{""TypeDiscriminator"":2,""OfficeNumber"":""77a"",""Name"":""E""}";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new PersonConverterWithTypeDiscriminator());

            {
                Person person = await Deserializer.DeserializeWrapper<Person>(customerJson, options);
                Assert.IsType<Customer>(person);
                Assert.Equal(100, ((Customer)person).CreditLimit);
                Assert.Equal("C", person.Name);

                string json = await Serializer.SerializeWrapper(person, options);
                Assert.Equal(customerJson, json);
            }

            {
                Person person = await Deserializer.DeserializeWrapper<Person>(employeeJson, options);
                Assert.IsType<Employee>(person);
                Assert.Equal("77a", ((Employee)person).OfficeNumber);
                Assert.Equal("E", person.Name);

                string json = await Serializer.SerializeWrapper(person, options);
                Assert.Equal(employeeJson, json);
            }
        }

        [Fact]
        public async Task NullPersonConverterPolymorphicTypeDiscriminator()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new PersonConverterWithTypeDiscriminator());

            Person person = await Deserializer.DeserializeWrapper<Person>("null");
            Assert.Null(person);
        }

        // A converter that can serialize an abstract Person type.
        private class PersonPolymorphicSerializerConverter : JsonConverter<Person>
        {
            public override Person Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException($"Deserializing not supported. Type={typeToConvert}.");
            }

            public override void Write(Utf8JsonWriter writer, Person value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }

        [Fact]
        public async Task PersonConverterSerializerPolymorphic()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new PersonPolymorphicSerializerConverter());

            Customer customer = new Customer
            {
                Name = "C",
                CreditLimit = 100
            };

            {
                // Verify the polymorphic case.
                Person person = customer;

                string json = await Serializer.SerializeWrapper(person, options);
                Assert.Contains(@"""CreditLimit"":100", json);
                Assert.Contains(@"""Name"":""C""", json);
                await Assert.ThrowsAsync<NotSupportedException>(async () => await Deserializer.DeserializeWrapper<Person>(json, options));

                string arrayJson = await Serializer.SerializeWrapper(new Person[] { person }, options);
                Assert.Contains(@"""CreditLimit"":100", arrayJson);
                Assert.Contains(@"""Name"":""C""", arrayJson);
                await Assert.ThrowsAsync<NotSupportedException>(async () => await Deserializer.DeserializeWrapper<Person[]>(arrayJson, options));
            }

            {
                // Ensure (de)serialization still works when using a Person-derived type. This does not call the custom converter.
                string json = await Serializer.SerializeWrapper(customer, options);
                Assert.Contains(@"""CreditLimit"":100", json);
                Assert.Contains(@"""Name"":""C""", json);

                customer = await Deserializer.DeserializeWrapper<Customer>(json, options);
                Assert.Equal(100, customer.CreditLimit);
                Assert.Equal("C", customer.Name);

                string arrayJson = await Serializer.SerializeWrapper(new Customer[] { customer }, options);
                Assert.Contains(@"""CreditLimit"":100", arrayJson);
                Assert.Contains(@"""Name"":""C""", arrayJson);

                Customer[] customers = await Deserializer.DeserializeWrapper<Customer[]>(arrayJson, options);
                Assert.Equal(100, customers[0].CreditLimit);
                Assert.Equal("C", customers[0].Name);
            }
        }
    }
}
