using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using VL.Core;
using VL.Core.EditorAttributes;
using VL.Lib.Collections;

namespace VL.PadInfo
{
    /// <summary>
    /// The runtime meta data of a single pad (a VL property) of a patched class or record.
    /// </summary>
    public sealed record PadInfo(
        string Name,
        IVLTypeInfo Type,
        object? Value,
        object? Default,
        object? Min,
        object? Max,
        int? Order,
        WidgetType? Widget,
        string? Label,
        string? Description,
        bool IsReadOnly,
        Spread<string> Tags,
        ImmutableDictionary<string, string> CustomMetaData,
        IVLPropertyInfo Property)
    {
        /// <summary>
        /// Writes the given value into this pad of the given instance.
        /// For a VL class the very same instance is returned (it got mutated), for a VL record a new instance is returned.
        /// </summary>
        public object SetValue(object instance, object? value)
            => PadInspector.SetPadValue(instance, Property, value);

        /// <summary>
        /// Writes the given value into this pad of the given instance and returns the updated pad info.
        /// </summary>
        public PadInfo WithValue(object instance, object? value, out object updatedInstance)
        {
            updatedInstance = SetValue(instance, value);
            return PadInspector.GetPad(Property, updatedInstance);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Name}:");
            sb.AppendLine($"  Value: {Value ?? "null"}");
            sb.AppendLine($"  Type: {Type?.Name}");
            sb.AppendLine($"  Default: {Default ?? "null"}");
            sb.AppendLine($"  Min: {Min ?? "null"}");
            sb.AppendLine($"  Max: {Max ?? "null"}");
            sb.AppendLine($"  Order: {(Order.HasValue ? Order.Value.ToString() : "null")}");
            sb.AppendLine($"  Widget: {(Widget.HasValue ? Widget.Value.ToString() : "null")}");
            if (!string.IsNullOrEmpty(Label)) sb.AppendLine($"  Label: {Label}");
            if (!string.IsNullOrEmpty(Description)) sb.AppendLine($"  Description: {Description}");
            if (IsReadOnly) sb.AppendLine("  ReadOnly: true");
            if (Tags.Count > 0) sb.AppendLine($"  Tags: {string.Join(", ", Tags)}");
            foreach (var kv in CustomMetaData)
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Reads the meta data of the pads (properties) of a patched VL class or record purely from
    /// runtime information - no access to the .vl document is required.
    /// The values originate from the attributes in the VL.Core.EditorAttributes namespace which
    /// the VL compiler emits onto the generated properties.
    /// </summary>
    public static class PadInspector
    {
        /// <summary>
        /// Returns the meta data of all pads of the given instance, including the current values.
        /// </summary>
        public static Spread<PadInfo> GetPads(IVLObject? instance, bool includeSystemGenerated = false)
        {
            if (instance is null)
                return Spread<PadInfo>.Empty;

            return GetPads(instance.Type, instance, includeSystemGenerated);
        }

        /// <summary>
        /// Returns the meta data of all pads of the given type. Values are read from
        /// <paramref name="instance"/> if given, otherwise from the default instance of the type.
        /// </summary>
        public static Spread<PadInfo> GetPads(IVLTypeInfo? type, object? instance = null, bool includeSystemGenerated = false)
        {
            if (type is null)
                return Spread<PadInfo>.Empty;

            instance ??= TryGetDefaultInstance(type);

            var properties = includeSystemGenerated ? type.AllProperties : type.Properties;
            return properties
                .Where(p => includeSystemGenerated || !p.IsManaged)
                .Select(p => GetPad(p, instance))
                .ToSpread();
        }

        /// <summary>
        /// Returns the meta data of the pad with the given name or null if no such pad exists.
        /// </summary>
        public static PadInfo? GetPad(IVLObject? instance, string name)
        {
            var property = instance?.Type?.GetProperty(name);
            if (property is null)
                return null;

            return GetPad(property, instance);
        }

        /// <summary>
        /// Returns the meta data of the given property. The value is read from <paramref name="instance"/> if given.
        /// </summary>
        public static PadInfo GetPad(IVLPropertyInfo property, object? instance = null)
        {
            var propertyType = property.Type.ClrType;

            return new PadInfo(
                Name: property.OriginalName,
                Type: property.Type,
                Value: TryGetValue(property, instance),
                Default: GetAttributeValue(property.GetAttributes<DefaultAttribute>().FirstOrDefault(), propertyType),
                Min: GetAttributeValue(property.GetAttributes<MinAttribute>().FirstOrDefault(), propertyType),
                Max: GetAttributeValue(property.GetAttributes<MaxAttribute>().FirstOrDefault(), propertyType),
                Order: AttributeHelpers.GetOrder(property).ToNullable(),
                Widget: AttributeHelpers.GetWidgetType(property).ToNullable(),
                Label: AttributeHelpers.GetLabel(property).ValueOrDefault_ForReferenceType(),
                Description: AttributeHelpers.GetDescription(property).ValueOrDefault_ForReferenceType(),
                IsReadOnly: AttributeHelpers.GetIsReadOnly(property),
                Tags: AttributeHelpers.GetTags(property),
                CustomMetaData: AttributeHelpers.GetCustomMetaData(property),
                Property: property);
        }

        /// <summary>
        /// Writes the given value into the pad with the given name.
        /// For a VL class the very same instance is returned (it got mutated), for a VL record a new instance is returned.
        /// Throws in case no pad with that name exists.
        /// </summary>
        public static IVLObject SetPadValue(IVLObject instance, string name, object? value)
        {
            ArgumentNullException.ThrowIfNull(instance);

            var property = instance.Type?.GetProperty(name)
                ?? throw new ArgumentException($"No pad named '{name}' on {instance.Type?.FullName}.", nameof(name));

            return (IVLObject)SetPadValue(instance, property, value);
        }

        /// <summary>
        /// Writes the given value into the given pad.
        /// For a VL class the very same instance is returned (it got mutated), for a VL record a new instance is returned.
        /// </summary>
        public static object SetPadValue(object instance, IVLPropertyInfo property, object? value)
        {
            ArgumentNullException.ThrowIfNull(instance);
            ArgumentNullException.ThrowIfNull(property);

            return property.WithValue(instance, Coerce(value, property.Type.ClrType));
        }

        /// <summary>
        /// Tries to write the given value into the pad with the given name. Returns false if no such pad exists
        /// or the value could not be written.
        /// </summary>
        public static bool TrySetPadValue(IVLObject? instance, string name, object? value, out IVLObject? updatedInstance)
        {
            updatedInstance = instance;

            var property = instance?.Type?.GetProperty(name);
            if (instance is null || property is null)
                return false;

            try
            {
                updatedInstance = (IVLObject)SetPadValue(instance, property, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resets the pad with the given name to the value declared by its Default attribute.
        /// </summary>
        public static bool TryResetPadToDefault(IVLObject? instance, string name, out IVLObject? updatedInstance)
        {
            updatedInstance = instance;

            var property = instance?.Type?.GetProperty(name);
            if (instance is null || property is null)
                return false;

            var @default = GetAttributeValue(property.GetAttributes<DefaultAttribute>().FirstOrDefault(), property.Type.ClrType);
            if (@default is null)
                return false;

            return TrySetPadValue(instance, name, @default, out updatedInstance);
        }

        private static object? Coerce(object? value, Type? targetType)
        {
            if (targetType is null || value is null)
                return value;

            if (targetType.IsInstanceOfType(value))
                return value;

            var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (nonNullable.IsEnum && value is string enumName)
                return Enum.Parse(nonNullable, enumName, ignoreCase: true);

            if (value is IConvertible)
                return Convert.ChangeType(value, nonNullable, CultureInfo.InvariantCulture);

            return value;
        }

        /// <summary>
        /// Returns a human readable report of all pads of the given instance.
        /// </summary>
        public static string InspectPads(IVLObject? instance, bool includeSystemGenerated = false)
        {
            if (instance is null)
                return "Instance is null.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== Pads of {instance.Type?.FullName} ===");
            foreach (var pad in GetPads(instance, includeSystemGenerated))
                sb.Append(pad.ToString());
            return sb.ToString();
        }

        private static object? TryGetValue(IVLPropertyInfo property, object? instance)
        {
            if (instance is null)
                return null;

            try
            {
                return property.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static object? TryGetDefaultInstance(IVLTypeInfo type)
        {
            try
            {
                return type.GetDefaultValue();
            }
            catch
            {
                return null;
            }
        }

        private static object? GetAttributeValue(DefaultAttribute? attribute, Type? type)
            => attribute is null || type is null ? null : TryGet(() => attribute.GetValue(type));

        private static object? GetAttributeValue(MinAttribute? attribute, Type? type)
            => attribute is null || type is null ? null : TryGet(() => attribute.GetValue(type));

        private static object? GetAttributeValue(MaxAttribute? attribute, Type? type)
            => attribute is null || type is null ? null : TryGet(() => attribute.GetValue(type));

        private static object? TryGet(Func<object?> getValue)
        {
            try
            {
                return getValue();
            }
            catch
            {
                return null;
            }
        }
    }
}
