using System.Reflection;
using System.Text;
using VL.Core;

namespace VL.PadInfo
{
    public class VLTypeInspector
    {
        /// <summary>
        /// Returns a string containing everything defined inside an IVLTypeInfo object, 
        /// including VL properties and underlying CLR methods, parameters, and fields.
        /// </summary>
        public static string InspectTypeInfo(IVLTypeInfo typeInfo)
        {
            if (typeInfo == null) return "TypeInfo is null.";

            var sb = new StringBuilder();

            sb.AppendLine($"=== VL Type Info: {typeInfo.FullName} ===");
            sb.AppendLine($"Name: {typeInfo.Name}");
            sb.AppendLine($"Category: {typeInfo.Category}");
            sb.AppendLine($"Is Patched: {typeInfo.IsPatched}");

            // 1. Get the default instance to extract default pin values
            object defaultInstance = null;
            try
            {
                // Retrieves the default value of this type as defined by VL[cite: 1]
                defaultInstance = typeInfo.GetDefaultValue();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"\n[Warning] Could not create default instance: {ex.Message}");
            }

            // 2. Print VL Properties with their Default Values
            sb.AppendLine("\n--- VL Properties (with Default Values) ---");
            foreach (var prop in typeInfo.AllProperties)
            {
                string defaultValStr = "unknown (could not instantiate)";
                if (defaultInstance != null)
                {
                    try
                    {
                        // Gets the property value of the given default instance[cite: 1]
                        object val = prop.GetValue(defaultInstance);
                        defaultValStr = val != null ? val.ToString() : "null";
                    }
                    catch (Exception ex)
                    {
                        defaultValStr = $"<Error reading: {ex.Message}>";
                    }
                }

                sb.AppendLine($"- {prop.OriginalName} (Type: {prop.Type?.Name})");
                sb.AppendLine($"    Default Value: {defaultValStr}");
            }

            // 3. Print CLR Fields with their Default Values
            if (typeInfo.ClrType != null)
            {
                if (defaultInstance != null)
                {
                    sb.AppendLine("\n--- CLR Fields (with Default Values) ---");
                    var fields = typeInfo.ClrType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    foreach (var field in fields)
                    {
                        string defaultValStr;
                        try
                        {
                            object val = field.GetValue(defaultInstance);
                            defaultValStr = val != null ? val.ToString() : "null";
                        }
                        catch (Exception ex)
                        {
                            defaultValStr = $"<Error: {ex.Message}>";
                        }

                        string visibility = field.IsPublic ? "public" : "private/protected";
                        sb.AppendLine($"- {visibility} {field.FieldType.Name} {field.Name} = {defaultValStr}");
                    }
                }

                // 4. Print Expanded CLR Methods
                sb.AppendLine("\n--- Expanded CLR Methods ---");
                var methods = typeInfo.ClrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

                foreach (var method in methods)
                {
                    string access = "private";
                    if (method.IsPublic) access = "public";
                    else if (method.IsFamily) access = "protected";
                    else if (method.IsAssembly) access = "internal";
                    else if (method.IsFamilyOrAssembly) access = "protected internal";

                    string modifiers = "";
                    if (method.IsStatic) modifiers += "static ";
                    if (method.IsAbstract) modifiers += "abstract ";
                    if (method.IsVirtual && !method.IsFinal) modifiers += "virtual ";

                    string special = method.IsSpecialName ? "[SpecialName] " : "";

                    sb.Append($"- {special}{access} {modifiers}{method.ReturnType.Name} {method.Name}(");
                    AppendParameters(sb, method.GetParameters());
                    sb.AppendLine(")");

                    // Extract Attributes and their properties
                    var attributes = method.GetCustomAttributes(false);
                    foreach (var attr in attributes)
                    {
                        Type attrType = attr.GetType();
                        sb.AppendLine($"    [{attrType.Name}]");

                        var attrProps = attrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        foreach (var attrProp in attrProps)
                        {
                            try
                            {
                                object val = attrProp.GetValue(attr);
                                sb.AppendLine($"        {attrProp.Name}: {val ?? "null"}");
                            }
                            catch { /* Ignore unreadable properties */ }
                        }
                    }
                }
            }
            else
            {
                sb.AppendLine("\nNo underlying ClrType available for deep reflection.");
            }

            return sb.ToString();
        }

        private static void AppendParameters(StringBuilder sb, ParameterInfo[] parameters)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];

                string modifier = "";
                if (p.IsOut) modifier = "out ";
                else if (p.ParameterType.IsByRef) modifier = "ref ";

                string typeName = p.ParameterType.Name.TrimEnd('&');

                sb.Append($"{modifier}{typeName} {p.Name}");

                if (p.HasDefaultValue)
                {
                    sb.Append($" = {p.DefaultValue ?? "null"}");
                }

                if (i < parameters.Length - 1) sb.Append(", ");
            }
        }
    }
}
