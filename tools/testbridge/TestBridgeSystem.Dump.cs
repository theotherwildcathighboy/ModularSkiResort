// Reads ECS state as JSON by component name, for checking a test's result as data: what a query matches, and any entity's components.
// Types come from every loaded assembly, including SkiResortMod. This read-only reflection is for this dev tool only.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Common;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const int kDefaultLimit = 50;
        private const int kMaxDepth = 4;
        private const BindingFlags kFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo s_GetComponent = typeof(EntityManager).GetMethods()
            .First(m => m.Name == nameof(EntityManager.GetComponentData) && m.IsGenericMethod
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Entity));

        private static readonly MethodInfo s_GetBuffer = typeof(EntityManager).GetMethods()
            .First(m => m.Name == nameof(EntityManager.GetBuffer) && m.IsGenericMethod
                && m.GetParameters()[0].ParameterType == typeof(Entity));

        private readonly Dictionary<string, Type> m_TypeCache = new Dictionary<string, Type>();

        /// <summary>{all: [names], none: [names], show: [names], limit}. The query leaves out deleted entities unless the call gives "none".</summary>
        private JObject DumpQuery(JObject args)
        {
            ComponentType[] all = Names(args["all"]).Select(n => ComponentType.ReadOnly(FindType(n))).ToArray();
            if (all.Length == 0)
                throw new ArgumentException("all must name at least one component");
            ComponentType[] none = args["none"] != null
                ? Names(args["none"]).Select(n => ComponentType.ReadOnly(FindType(n))).ToArray()
                : new[] { ComponentType.ReadOnly<Deleted>() };
            Type[] show = Names(args["show"]).Select(FindType).ToArray();
            int limit = (int?)args["limit"] ?? kDefaultLimit;

            EntityQuery query = EntityManager.CreateEntityQuery(new EntityQueryDesc { All = all, None = none });
            try
            {
                using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
                var rows = new JArray();
                foreach (Entity entity in entities.Take(limit))
                {
                    var row = new JObject { ["entity"] = EntityId(entity), ["prefab"] = PrefabNameOf(entity) };
                    foreach (Type type in show)
                        row[type.Name] = EntityManager.HasComponent(entity, ComponentType.ReadOnly(type)) ? ReadComponent(entity, type) : null;
                    rows.Add(row);
                }
                return new JObject { ["count"] = entities.Length, ["rows"] = rows };
            }
            finally
            {
                query.Dispose();
            }
        }

        /// <summary>{id: "index:version", only: [names]}: the entity's component names, with values for those in "only" (all when absent).</summary>
        private JObject DumpEntity(JObject args)
        {
            string[] id = ((string)args["id"]).Split(':');
            var entity = new Entity { Index = int.Parse(id[0]), Version = int.Parse(id[1]) };
            if (!EntityManager.Exists(entity))
                throw new ArgumentException($"entity {args["id"]} does not exist");
            HashSet<Type> only = args["only"] != null ? new HashSet<Type>(Names(args["only"]).Select(FindType)) : null;

            var components = new JObject();
            using NativeArray<ComponentType> types = EntityManager.GetComponentTypes(entity, Allocator.Temp);
            foreach (ComponentType componentType in types)
            {
                Type type = componentType.GetManagedType();
                if (type != null)
                    components[type.FullName] = only == null || only.Contains(type) ? ReadComponent(entity, type) : "";
            }
            return new JObject { ["entity"] = EntityId(entity), ["prefab"] = PrefabNameOf(entity), ["components"] = components };
        }

        private static IEnumerable<string> Names(JToken token)
        {
            return token == null ? Enumerable.Empty<string>() : token.Select(t => (string)t);
        }

        /// <summary>A full name, or a short name when it matches one component type (the error lists the candidates otherwise).</summary>
        private Type FindType(string name)
        {
            if (m_TypeCache.TryGetValue(name, out Type cached))
                return cached;
            List<Type> matches = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(LoadableTypes)
                .Where(t => t.IsValueType && (t.FullName == name || t.Name == name)
                    && (typeof(IComponentData).IsAssignableFrom(t) || typeof(IBufferElementData).IsAssignableFrom(t)))
                .ToList();
            Type exact = matches.FirstOrDefault(t => t.FullName == name);
            if (exact != null)
                return m_TypeCache[name] = exact;
            if (matches.Count != 1)
                throw new ArgumentException($"component '{name}' matches {matches.Count} types: {string.Join(", ", matches.Select(t => t.FullName))}");
            return m_TypeCache[name] = matches[0];
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        private JToken ReadComponent(Entity entity, Type type)
        {
            try
            {
                if (typeof(IBufferElementData).IsAssignableFrom(type))
                {
                    object buffer = s_GetBuffer.MakeGenericMethod(type).Invoke(EntityManager, new object[] { entity, true });
                    var elements = new JArray();
                    int length = (int)buffer.GetType().GetProperty("Length").GetValue(buffer);
                    PropertyInfo item = buffer.GetType().GetProperty("Item");
                    for (int i = 0; i < length && i < kDefaultLimit; i++)
                        elements.Add(Serialize(item.GetValue(buffer, new object[] { i }), 0));
                    return new JObject { ["length"] = length, ["elements"] = elements };
                }
                if (!typeof(IComponentData).IsAssignableFrom(type) || !type.IsValueType)
                    return "(not read)";
                if (type.GetFields(kFields).Length == 0)
                    return "(tag)";
                return Serialize(s_GetComponent.MakeGenericMethod(type).Invoke(EntityManager, new object[] { entity }), 0);
            }
            catch (Exception e)
            {
                return $"(read failed: {(e.InnerException ?? e).Message})";
            }
        }

        private static JToken Serialize(object value, int depth)
        {
            switch (value)
            {
                case null: return JValue.CreateNull();
                case Entity entity: return EntityId(entity);
                case Enum e: return e.ToString();
                case string s: return s;
                case float f: return float.IsNaN(f) || float.IsInfinity(f) ? f.ToString() : (JToken)Math.Round(f, 3);
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? d.ToString() : (JToken)Math.Round(d, 3);
            }
            Type type = value.GetType();
            if (type.IsPrimitive)
                return JToken.FromObject(value);
            if (depth >= kMaxDepth || type.IsPointer || type == typeof(IntPtr) || !type.IsValueType || value is IEnumerable
                || type.Name.Contains("Blob") || type.Name.StartsWith("Native"))
                return $"({type.Name})";
            var result = new JObject();
            foreach (FieldInfo field in type.GetFields(kFields))
            {
                if (!field.FieldType.IsPointer)
                    result[field.Name] = Serialize(field.GetValue(value), depth + 1);
            }
            return result;
        }
    }
}
