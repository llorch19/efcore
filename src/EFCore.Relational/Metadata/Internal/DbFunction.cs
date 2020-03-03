// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class DbFunction : IMutableDbFunction, IConventionDbFunction
    {
        private readonly IMutableModel _model;
        private readonly List<DbFunctionParameter> _parameters;
        private string _schema;
        private string _name;
        private string _storeType;
        private RelationalTypeMapping _typeMapping;
        private Func<IReadOnlyCollection<SqlExpression>, SqlExpression> _translation;

        private ConfigurationSource _configurationSource;
        private ConfigurationSource? _schemaConfigurationSource;
        private ConfigurationSource? _nameConfigurationSource;
        private ConfigurationSource? _storeTypeConfigurationSource;
        private ConfigurationSource? _typeMappingConfigurationSource;
        private ConfigurationSource? _translationConfigurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public DbFunction(
            [NotNull] MethodInfo methodInfo,
            [NotNull] IMutableModel model,
            ConfigurationSource configurationSource)
        {
            if (methodInfo.IsGenericMethod)
            {
                throw new ArgumentException(RelationalStrings.DbFunctionGenericMethodNotSupported(methodInfo.DisplayName()));
            }

            if (!methodInfo.IsStatic
                && !typeof(DbContext).IsAssignableFrom(methodInfo.DeclaringType))
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                throw new ArgumentException(
                    RelationalStrings.DbFunctionInvalidInstanceType(
                        methodInfo.DisplayName(), methodInfo.DeclaringType.ShortDisplayName()));
            }

            if (methodInfo.ReturnType == null
                || methodInfo.ReturnType == typeof(void))
            {
                throw new ArgumentException(
                    RelationalStrings.DbFunctionInvalidReturnType(
                        methodInfo.DisplayName(), methodInfo.ReturnType.ShortDisplayName()));
            }

            if (methodInfo.ReturnType.IsGenericType
                && methodInfo.ReturnType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            {
                IsIQueryable = true;

                //todo - if the generic argument is not usuable as an entitytype should we throw here?  IE IQueryable<int>
                //the built in entitytype will throw is the type is not a class
                if (model.FindEntityType(methodInfo.ReturnType.GetGenericArguments()[0]) == null)
                {
                    model.AddEntityType(methodInfo.ReturnType.GetGenericArguments()[0]).SetAnnotation(RelationalAnnotationNames.QueryableFunctionResultType, null);
                }
            }

            MethodInfo = methodInfo;

            var parameters = methodInfo.GetParameters();

            _parameters = parameters
                .Select((pi, i) => new DbFunctionParameter(this, pi.Name, pi.ParameterType))
                .ToList();

            ModelName = GetFunctionName(methodInfo, parameters);

            _model = model;
            _configurationSource = configurationSource;
            Builder = new DbFunctionBuilder(this);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public DbFunction(
            [NotNull] string name,
            [NotNull] IMutableModel model,
            ConfigurationSource configurationSource)
        {
            ModelName = name;
            _parameters = new List<DbFunctionParameter>();
            _model = model;
            _configurationSource = configurationSource;
            Builder = new DbFunctionBuilder(this);
        }

        private static string GetFunctionName(MethodInfo methodInfo, ParameterInfo[] parameters)
            => methodInfo.DeclaringType.FullName + "." + methodInfo.Name
                + "(" + string.Join(",", parameters.Select(p => p.ParameterType.FullName)) + ")";

        /// <summary>
        ///     The builder that can be used to configure this function.
        /// </summary>
        public virtual DbFunctionBuilder Builder { get; private set; }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static IEnumerable<DbFunction> GetDbFunctions([NotNull] IModel model)
            => ((SortedDictionary<string, DbFunction>)model[RelationalAnnotationNames.DbFunctions])
                ?.Values ?? Enumerable.Empty<DbFunction>();

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static DbFunction FindDbFunction([NotNull] IModel model, [NotNull] MethodInfo methodInfo)
            => model[RelationalAnnotationNames.DbFunctions] is SortedDictionary<string, DbFunction> functions
               && functions.TryGetValue(GetFunctionName(methodInfo, methodInfo.GetParameters()), out var dbFunction)
                ? dbFunction
                : null;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static DbFunction FindDbFunction([NotNull] IModel model, [NotNull] string name)
            => model[RelationalAnnotationNames.DbFunctions] is SortedDictionary<string, DbFunction> functions
               && functions.TryGetValue(name, out var dbFunction)
                ? dbFunction
                : null;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static DbFunction AddDbFunction(
            [NotNull] IMutableModel model, [NotNull] MethodInfo methodInfo, ConfigurationSource configurationSource)
        {
            var function = new DbFunction(methodInfo, model, configurationSource);

            GetOrCreateFunctions(model).Add(function.ModelName, function);
            return function;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static DbFunction AddDbFunction(
            [NotNull] IMutableModel model,
            [NotNull] string name,
            ConfigurationSource configurationSource)
        {
            var function = new DbFunction(name, model, configurationSource);

            GetOrCreateFunctions(model).Add(name, function);
            return function;
        }

        private static SortedDictionary<string, DbFunction> GetOrCreateFunctions(IMutableModel model)
            => (SortedDictionary<string, DbFunction>)(
                model[RelationalAnnotationNames.DbFunctions] ??= new SortedDictionary<string, DbFunction>());

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static DbFunction RemoveDbFunction(
            [NotNull] IMutableModel model,
            [NotNull] MethodInfo methodInfo)
        {
            if (model[RelationalAnnotationNames.DbFunctions] is SortedDictionary<string, DbFunction> functions)
            {
                var name = GetFunctionName(methodInfo, methodInfo.GetParameters());
                if (functions.TryGetValue(name, out var function))
                {
                    functions.Remove(name);
                    function.Builder = null;

                    return function;
                }
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static DbFunction RemoveDbFunction(
            [NotNull] IMutableModel model,
            [NotNull] string name)
        {
            if (model[RelationalAnnotationNames.DbFunctions] is SortedDictionary<string, DbFunction> functions
                && functions.TryGetValue(name, out var function))
            {
                functions.Remove(name);
                function.Builder = null;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual string ModelName { get; private set; }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual MethodInfo MethodInfo { get; }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual ConfigurationSource GetConfigurationSource()
            => _configurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void UpdateConfigurationSource(ConfigurationSource configurationSource)
            => _configurationSource = configurationSource.Max(_configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual string Schema
        {
            get => _schema ?? _model.GetDefaultSchema();
            set => SetSchema(value, ConfigurationSource.Explicit);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void SetSchema([CanBeNull] string schema, ConfigurationSource configurationSource)
        {
            _schema = schema;

            _schemaConfigurationSource = schema == null
                ? (ConfigurationSource?)null
                : configurationSource.Max(_schemaConfigurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual ConfigurationSource? GetSchemaConfigurationSource() => _schemaConfigurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual string Name
        {
            get => _name ?? MethodInfo?.Name ?? ModelName;
            set => SetName(value, ConfigurationSource.Explicit);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void SetName([CanBeNull] string name, ConfigurationSource configurationSource)
        {
            _name = name;

            _nameConfigurationSource = name == null
                ? (ConfigurationSource?)null
                : configurationSource.Max(_nameConfigurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual ConfigurationSource? GetNameConfigurationSource() => _nameConfigurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual string StoreType
        {
            get => _storeType;
            set => SetStoreType(value, ConfigurationSource.Explicit);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void SetStoreType([CanBeNull] string storeType, ConfigurationSource configurationSource)
        {
            _storeType = storeType;

            _storeTypeConfigurationSource = storeType == null
                ? (ConfigurationSource?)null
                : configurationSource.Max(_storeTypeConfigurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual ConfigurationSource? GetStoreTypeConfigurationSource() => _storeTypeConfigurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual RelationalTypeMapping TypeMapping
        {
            get => _typeMapping;
            set => SetTypeMapping(value, ConfigurationSource.Explicit);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void SetTypeMapping([CanBeNull] RelationalTypeMapping typeMapping, ConfigurationSource configurationSource)
        {
            _typeMapping = typeMapping;

            _typeMappingConfigurationSource = typeMapping == null
                ? (ConfigurationSource?)null
                : configurationSource.Max(_typeMappingConfigurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual ConfigurationSource? GetTypeMappingConfigurationSource() => _typeMappingConfigurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual Func<IReadOnlyCollection<SqlExpression>, SqlExpression> Translation
        {
            get => _translation;
            set => SetTranslation(value, ConfigurationSource.Explicit);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual void SetTranslation(
            [CanBeNull] Func<IReadOnlyCollection<SqlExpression>, SqlExpression> translation,
            ConfigurationSource configurationSource)
        {
            _translation = translation;

            _translationConfigurationSource = translation == null
                ? (ConfigurationSource?)null
                : configurationSource.Max(_translationConfigurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual ConfigurationSource? GetTranslationConfigurationSource() => _translationConfigurationSource;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool IsIQueryable { get; }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IConventionDbFunctionBuilder IConventionDbFunction.Builder => Builder;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IModel IDbFunction.Model => _model;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IMutableModel IMutableDbFunction.Model => _model;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IConventionModel IConventionDbFunction.Model => (IConventionModel)_model;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        void IConventionDbFunction.SetName(string name, bool fromDataAnnotation)
            => SetName(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        void IConventionDbFunction.SetSchema(string schema, bool fromDataAnnotation)
            => SetSchema(schema, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        void IConventionDbFunction.SetStoreType(string storeType, bool fromDataAnnotation)
            => SetStoreType(storeType, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        void IConventionDbFunction.SetTypeMapping(RelationalTypeMapping returnTypeMapping, bool fromDataAnnotation)
            => SetTypeMapping(returnTypeMapping, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        void IConventionDbFunction.SetTranslation(
            Func<IReadOnlyCollection<SqlExpression>, SqlExpression> translation, bool fromDataAnnotation)
            => SetTranslation(translation, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IReadOnlyList<IDbFunctionParameter> Parameters => _parameters;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IReadOnlyList<IConventionDbFunctionParameter> IConventionDbFunction.Parameters => _parameters;
    }
}
