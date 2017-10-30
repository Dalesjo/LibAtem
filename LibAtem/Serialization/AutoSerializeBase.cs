using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LibAtem.Serialization
{
    public abstract class AutoSerializeBase
    {
        private static readonly Dictionary<Type, CommandPropertySpec> _propertySpecCache;

        static AutoSerializeBase()
        {
            _propertySpecCache = new Dictionary<Type, CommandPropertySpec>();
        }

        public static void CompilePropertySpecForTypes(IEnumerable<Type> types)
        {
            foreach (Type t in types)
                BuildPropertySpecForType(t);
        }

        private static CommandPropertySpec BuildPropertySpecForType(Type t)
        {
            int length = GetLengthFromAttribute(t);
            var props = new List<PropertySpec>();

            var rawProps = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(prop => prop.GetCustomAttribute<NoSerializeAttribute>() == null);
            foreach (PropertyInfo prop in rawProps)
            {
                SerializeAttribute attr = prop.GetCustomAttribute<SerializeAttribute>();
                if (attr == null) // This means the property shouldnt be serialized
                    continue;

                SerializableAttributeBase serAttr = prop.GetCustomAttribute<SerializableAttributeBase>();
                if (serAttr == null)
                    throw new SerializationException(t.Name, "Missing serialization definition on property {0}", prop.Name);

                Delegate setter = prop.CanWrite && prop.GetSetMethod() != null ? Delegate.CreateDelegate(Expression.GetActionType(t, prop.PropertyType), prop.GetSetMethod()) : null;
                Delegate getter = prop.GetGetMethod() != null ? Delegate.CreateDelegate(Expression.GetFuncType(t, prop.PropertyType), prop.GetGetMethod()) : null;

                props.Add(new PropertySpec(setter, getter, serAttr, attr, prop));
            }

            return _propertySpecCache[t] = new CommandPropertySpec(length, props);
        }

        private class CommandPropertySpec
        {
            public int Length { get; }
            public List<PropertySpec> Properties { get; }

            public CommandPropertySpec(int length, List<PropertySpec> properties)
            {
                Length = length;
                Properties = properties;
            }
        }

        private class PropertySpec
        {
            public Delegate Setter { get; }
            public Delegate Getter { get; }
            public SerializableAttributeBase SerAttr { get; }
            public SerializeAttribute Attr { get; }
            public PropertyInfo PropInfo { get; }

            public PropertySpec(Delegate setter, Delegate getter, SerializableAttributeBase serAttr, SerializeAttribute attr, PropertyInfo propInfo)
            {
                Setter = setter;
                Getter = getter;
                SerAttr = serAttr;
                Attr = attr;
                PropInfo = propInfo;
            }
        }
        
        private static int GetLengthFromAttribute(Type t)
        {
            return t.GetTypeInfo().GetCustomAttribute<LengthAttribute>()?.Length ?? -1;
        }

        protected virtual int GetLength()
        {
            if (_propertySpecCache.TryGetValue(GetType(), out CommandPropertySpec info))
                return info.Length;

            return GetLengthFromAttribute(GetType());
        }

        public virtual void Serialize(ByteArrayBuilder cmd)
        {
            if (!_propertySpecCache.TryGetValue(GetType(), out CommandPropertySpec info))
                info = BuildPropertySpecForType(GetType());

            int length = GetLength();
            if (length < 0)
                throw new SerializationException(GetType().Name, "Cannot auto serialize without a defined length");

            var res = new byte[length];
            foreach (PropertySpec prop in info.Properties)
            {
                if (prop.Getter != null)
                    prop.SerAttr.Serialize(cmd.ReverseBytes, res, prop.Attr.StartByte, prop.Getter.DynamicInvoke(this));
            }
            
            cmd.AddByte(res);
        }

        public virtual void Deserialize(ParsedByteArray cmd)
        {
            if (!_propertySpecCache.TryGetValue(GetType(), out CommandPropertySpec info))
                info = BuildPropertySpecForType(GetType());

            int attrLength = info.Length;
            if (attrLength != -1 && attrLength != cmd.BodyLength)
                throw new Exception("Auto deserialize length mismatch");

            foreach (PropertySpec prop in info.Properties)
            {
                if (prop.Setter != null)
                    prop.Setter.DynamicInvoke(this, prop.SerAttr.Deserialize(cmd.ReverseBytes, cmd.Body, prop.Attr.StartByte, prop.PropInfo));
            }

            if (GetLength() != cmd.BodyLength)
                throw new Exception("Auto deserialize final length mismatch");
        }
    }

    public class SerializationException : Exception
    {
        public string CommandName { get; }

        public SerializationException(string cmdName, string fmt, params object[] vals) : base(
            string.Format(string.Format("{0}: {1}", cmdName, fmt), vals))
        {
            CommandName = cmdName;
        }
    }

    public class LengthAttribute : Attribute
    {
        public int Length { get; }

        public LengthAttribute(int length)
        {
            Length = length;
        }
    }
}