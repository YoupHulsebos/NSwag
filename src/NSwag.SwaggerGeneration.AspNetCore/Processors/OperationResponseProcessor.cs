﻿//-----------------------------------------------------------------------
// <copyright file="OperationResponseProcessor.cs" company="NSwag">
//     Copyright (c) Rico Suter. All rights reserved.
// </copyright>
// <license>https://github.com/NSwag/NSwag/blob/master/LICENSE.md</license>
// <author>Rico Suter, mail@rsuter.com</author>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Namotion.Reflection;
using NJsonSchema;
using NSwag.SwaggerGeneration.Processors;
using NSwag.SwaggerGeneration.Processors.Contexts;

namespace NSwag.SwaggerGeneration.AspNetCore.Processors
{
    /// <summary>Generates the operation's response objects based on reflection and the ResponseTypeAttribute, SwaggerResponseAttribute and ProducesResponseTypeAttribute attributes.</summary>
    public class OperationResponseProcessor : OperationResponseProcessorBase, IOperationProcessor
    {
        private readonly AspNetCoreToSwaggerGeneratorSettings _settings;

        /// <summary>Initializes a new instance of the <see cref="OperationParameterProcessor"/> class.</summary>
        /// <param name="settings">The settings.</param>
        public OperationResponseProcessor(AspNetCoreToSwaggerGeneratorSettings settings)
            : base(settings)
        {
            _settings = settings;
        }

        /// <summary>Processes the specified method information.</summary>
        /// <param name="operationProcessorContext"></param>
        /// <returns>true if the operation should be added to the Swagger specification.</returns>
        public async Task<bool> ProcessAsync(OperationProcessorContext operationProcessorContext)
        {
            if (!(operationProcessorContext is AspNetCoreOperationProcessorContext context))
            {
                return false;
            }

            var responseTypeAttributes = context.MethodInfo
                .GetCustomAttributes()
                .Where(a => a.GetType().IsAssignableToTypeName("ResponseTypeAttribute", TypeNameStyle.Name) ||
                            a.GetType().IsAssignableToTypeName("SwaggerResponseAttribute", TypeNameStyle.Name) ||
                            a.GetType().IsAssignableToTypeName("SwaggerDefaultResponseAttribute", TypeNameStyle.Name))
                .Concat(context.MethodInfo.DeclaringType.GetTypeInfo().GetCustomAttributes()
                    .Where(a => a.GetType().IsAssignableToTypeName("SwaggerResponseAttribute", TypeNameStyle.Name) ||
                                a.GetType().IsAssignableToTypeName("SwaggerDefaultResponseAttribute", TypeNameStyle.Name)))
                .ToList();

            if (responseTypeAttributes.Count > 0)
            {
                // if SwaggerResponseAttribute \ ResponseTypeAttributes are present, we'll only use those.
                await ProcessResponseTypeAttributes(context, responseTypeAttributes);
            }
            else
            {
                foreach (var apiResponse in context.ApiDescription.SupportedResponseTypes)
                {
                    var returnType = apiResponse.Type;
                    var response = new OpenApiResponse();
                    string httpStatusCode;

                    if (apiResponse.TryGetPropertyValue<bool>("IsDefaultResponse"))
                    {
                        httpStatusCode = "default";
                    }
                    else if (apiResponse.StatusCode == 0 && IsVoidResponse(returnType))
                    {
                        httpStatusCode = "200";
                    }
                    else
                    {
                        httpStatusCode = apiResponse.StatusCode.ToString(CultureInfo.InvariantCulture);
                    }

                    if (IsVoidResponse(returnType) == false)
                    {
                        var returnTypeAttributes = context.MethodInfo.ReturnParameter?.GetCustomAttributes(false).OfType<Attribute>();
                        var contextualReturnType = returnType.ToContextualType(returnTypeAttributes);

                        var typeDescription = _settings.ReflectionService.GetDescription(
                            contextualReturnType, _settings.DefaultResponseReferenceTypeNullHandling, _settings);

                        response.IsNullableRaw = typeDescription.IsNullable;
                        response.Schema = await context.SchemaGenerator
                            .GenerateWithReferenceAndNullabilityAsync<JsonSchema>(contextualReturnType, typeDescription.IsNullable, context.SchemaResolver)
                            .ConfigureAwait(false);
                    }

                    context.OperationDescription.Operation.Responses[httpStatusCode] = response;
                }
            }

            if (context.OperationDescription.Operation.Responses.Count == 0)
            {
                context.OperationDescription.Operation.Responses[GetVoidResponseStatusCode()] = new OpenApiResponse
                {
                    IsNullableRaw = true,
                    Schema = new JsonSchema
                    {
                        Type = _settings.SchemaType == SchemaType.Swagger2 ? JsonObjectType.File : JsonObjectType.String,
                        Format = _settings.SchemaType == SchemaType.Swagger2 ? null : JsonFormatStrings.Binary,
                    }
                };
            }

            await UpdateResponseDescriptionAsync(context);
            return true;
        }

        /// <summary>Gets the response HTTP status code for an empty/void response and the given generator.</summary>
        /// <returns>The status code.</returns>
        protected override string GetVoidResponseStatusCode()
        {
            return "200";
        }

        private bool IsVoidResponse(Type returnType)
        {
            return returnType == null || returnType.FullName == "System.Void";
        }
    }
}