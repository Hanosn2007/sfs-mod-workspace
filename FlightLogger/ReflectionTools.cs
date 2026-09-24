using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FlightLogger
{
    internal static class ReflectionTools
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static Type Type(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(fullName, false))
                .FirstOrDefault(t => t != null);
        }

        public static object GetMember(object target, string name)
        {
            if (target == null)
                return null;

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;

            FieldInfo field = type.GetField(name, Flags);
            if (field != null)
                return field.GetValue(instance);

            PropertyInfo property = type.GetProperty(name, Flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            return null;
        }

        public static object GetValueObject(object wrapper)
        {
            return GetMember(wrapper, "Value");
        }

        public static double? AsDouble(object value)
        {
            if (value == null)
                return null;

            object unwrapped = GetValueObject(value) ?? value;

            try
            {
                if (unwrapped is IConvertible)
                    return Convert.ToDouble(unwrapped, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }

            return null;
        }

        public static bool? AsBool(object value)
        {
            if (value == null)
                return null;

            object unwrapped = GetValueObject(value) ?? value;

            if (unwrapped is bool b)
                return b;

            return null;
        }

        public static string AsString(object value)
        {
            object unwrapped = GetValueObject(value) ?? value;
            if (unwrapped == null)
                return null;

            return unwrapped.ToString();
        }

        public static double? X(object vector)
        {
            return AsDouble(GetMember(vector, "x"));
        }

        public static double? Y(object vector)
        {
            return AsDouble(GetMember(vector, "y"));
        }

        public static double? Magnitude(object vector)
        {
            return AsDouble(GetMember(vector, "magnitude"));
        }

        public static Array GetModules(object partHolder, string moduleTypeName)
        {
            if (partHolder == null)
                return null;

            Type moduleType = Type(moduleTypeName);
            if (moduleType == null)
                return null;

            MethodInfo method = partHolder.GetType()
                .GetMethods(Flags)
                .FirstOrDefault(m => m.Name == "GetModules" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

            if (method == null)
                return null;

            object result = method.MakeGenericMethod(moduleType).Invoke(partHolder, null);
            return result as Array;
        }

        public static object TryCreateOrbit(object location)
        {
            Type orbitType = Type("SFS.World.Orbit");
            if (orbitType == null || location == null)
                return null;

            MethodInfo method = orbitType.GetMethod("TryCreateOrbit", Flags);
            if (method == null)
                return null;

            object[] args = { location, true, false, false };
            object orbit = method.Invoke(null, args);
            bool success = args[3] is bool b && b;
            return success ? orbit : null;
        }

        public static string DescribeObject(object value, int maxMembers = 120)
        {
            if (value == null)
                return "<null>";

            StringBuilder sb = new StringBuilder();
            Type type = value.GetType();
            sb.AppendLine(type.FullName);

            foreach (FieldInfo field in type.GetFields(Flags).Where(f => !f.Name.Contains("<")).Take(maxMembers))
            {
                sb.Append("  field ");
                sb.Append(field.FieldType.Name);
                sb.Append(' ');
                sb.Append(field.Name);
                sb.Append(" = ");
                sb.AppendLine(SafeValue(() => field.GetValue(value)));
            }

            foreach (PropertyInfo property in type.GetProperties(Flags).Where(p => p.GetIndexParameters().Length == 0).Take(maxMembers))
            {
                sb.Append("  property ");
                sb.Append(property.PropertyType.Name);
                sb.Append(' ');
                sb.Append(property.Name);
                sb.Append(" = ");
                sb.AppendLine(SafeValue(() => property.GetValue(value, null)));
            }

            return sb.ToString();
        }

        public static string SafeValue(Func<object> read)
        {
            try
            {
                object value = read();
                if (value == null)
                    return "<null>";

                if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal)
                    return Convert.ToString(value, CultureInfo.InvariantCulture);

                if (value is IEnumerable enumerable && !(value is string))
                {
                    int count = 0;
                    foreach (object _ in enumerable)
                        count++;
                    return value.GetType().Name + " Count=" + count;
                }

                return value.GetType().FullName;
            }
            catch (Exception ex)
            {
                return "<read failed: " + ex.GetType().Name + ">";
            }
        }
    }
}
