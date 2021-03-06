﻿using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkMock.Internal
{
    internal class AttributeBasedKeyFactoryBuilder<TAttribute> : IKeyFactoryBuilder
        where TAttribute : Attribute
    {
        public Func<T, KeyContext, object> BuildKeyFactory<T>()
        {
            var entityType = typeof(T);
            var keyProperties = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.GetCustomAttribute(typeof(TAttribute)) != null)
                .ToArray();

            if (!keyProperties.Any()) throw new InvalidOperationException($"Entity type {entityType.Name} does not contain any property marked with {typeof(TAttribute).Name}");

            var keyFactory = BuildIdentityKeyFactory<T>(keyProperties);
            keyFactory = keyFactory ?? BuildDefaultKeyFactory<T>(keyProperties);
            return keyFactory;
        }

        private static Func<T, KeyContext, object> BuildIdentityKeyFactory<T>(PropertyInfo[] keyProperties)
        {
            if (keyProperties.Length != 1) return null;
            var keyProperty = keyProperties[0];
            if (keyProperty == null) return null;
            var databaseGeneratedAttribute = keyProperty.GetCustomAttribute(typeof(DatabaseGeneratedAttribute)) as DatabaseGeneratedAttribute;
            if (databaseGeneratedAttribute?.DatabaseGeneratedOption != DatabaseGeneratedOption.Identity) return null;
            if (!typeof(long).IsAssignableFrom(keyProperty.PropertyType)) return null;

            return (entity, keyContext) =>
            {
                var keyValue = (long)keyProperty.GetValue(entity);
                if (keyValue == 0)
                {
                    keyValue = keyContext.NextIdentity;
                    keyProperty.SetValue(entity, keyValue);
                }

                return keyValue;
            };
        }

        private static Func<T, KeyContext, object> BuildDefaultKeyFactory<T>(PropertyInfo[] keyProperties)
        {
            var entityType = typeof(T);

            var tupleType = Type.GetType($"System.Tuple`{keyProperties.Length}");
            if (tupleType == null) throw new InvalidOperationException($"No tuple type found for {keyProperties.Length} generic arguments");

            var keyPropertyTypes = keyProperties.Select(x => x.PropertyType).ToArray();
            var constructor = tupleType.MakeGenericType(keyPropertyTypes).GetConstructor(keyPropertyTypes);
            if (constructor == null) throw new InvalidOperationException($"No tuple constructor found for key in {entityType.Name} entity");

            var entityArgument = Expression.Parameter(entityType);
            var keyContextArgument = Expression.Parameter(typeof(KeyContext));
            var newTupleExpression = Expression.New(constructor, keyProperties.Select(x => Expression.Property(entityArgument, x)));
            return Expression.Lambda<Func<T, KeyContext, object>>(newTupleExpression, entityArgument, keyContextArgument).Compile();
        }
    }
}
