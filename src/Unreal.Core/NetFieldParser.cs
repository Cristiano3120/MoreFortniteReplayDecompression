using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace Unreal.Core;

/// <summary>
/// Responsible for parsing received properties to the correct <see cref="Type"/> and setting the parsed value on the created object.
/// Only parses the properties marked with <see cref="NetFieldExportAttribute"/>.
/// </summary>
public class NetFieldParser
{
    private readonly NetGuidCache GuidCache;
    public HashSet<string> PlayerControllerGroups { get; private set; } = new();

    //private readonly Dictionary<string, NetFieldGroupInfo> _netFieldGroups = new();
    private readonly KeyList<string, NetFieldGroupInfo> NetFieldGroups = new();
    private readonly SingleInstanceExport[] _objects;
    private readonly Dictionary<Type, RepLayoutCmdType> _primitiveTypeLayout = new();
    private readonly Dictionary<string, ClassNetCacheInfo> _classNetCacheToNetFieldGroup = new();
    private readonly CompiledLinqCache _linqCache = new();
    private readonly IReadOnlyDictionary<Type, IReadOnlySet<string>>? _onlyProperties;

    // Groups registered by path prefix. A matching path is added to NetFieldGroups as an alias on first use.
    private readonly List<(string Prefix, NetFieldGroupInfo Info)> _prefixGroups = new();
    private readonly HashSet<string> _noPrefixMatch = new();

    /// <summary>
    /// Create a NetFieldParser, which will load all <see cref="NetFieldExportGroup"/> in the <see cref="AppDomain.CurrentDomain"/>.
    /// </summary>
    /// <param name="cache">Instance of NetGuidCache, used to resolve netguids to their string value.</param>
    /// <param name="mode"></param>
    /// <param name="assemblyNameFilter">Found assemblies should contain this string.</param>
    /// <param name="ignoredTypes">
    /// Export group types that are not parsed even if their parse mode would allow it. The parser skips their data completely,
    /// which saves time if the data is not needed (for example health or damage cues).
    /// </param>
    /// <param name="onlyProperties">
    /// Per export group type the names of the only properties that are read; all other properties of that type are skipped by
    /// their bit length without decoding them (they stay <c>null</c>). Types that are not in it are read completely.
    /// </param>
    public NetFieldParser(NetGuidCache cache, ParseMode mode, string assemblyNameFilter = "ReplayReader", IReadOnlySet<Type>? ignoredTypes = null,
        IReadOnlyDictionary<Type, IReadOnlySet<string>>? onlyProperties = null)
    {
        GuidCache = cache;
        _onlyProperties = onlyProperties;

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName.Contains(assemblyNameFilter) || a.FullName.Contains("Unreal.Core"))
            .SelectMany(i => i.GetTypes())
            .ToList();

        // Add all netfieldexportgroups
        var netFields = types.Where(c => c.GetCustomAttribute<NetFieldExportGroupAttribute>() != null);
        foreach (var type in netFields)
        {
            if (ignoredTypes?.Contains(type) == true)
            {
                continue;
            }

            var attribute = type.GetCustomAttribute<NetFieldExportGroupAttribute>();
            if (attribute.MinimalParseMode <= mode)
            {
                var info = new NetFieldGroupInfo
                {
                    Type = type,
                    TypeId = _linqCache.AddExportType(type),
                };

                NetFieldGroups.Add(attribute.Path, info);
                AddNetFieldInfo(type, info, mode);
            }
        }

        // Add all groups matched by path prefix
        var prefixFields = types.Where(c => c.GetCustomAttribute<NetFieldExportGroupPrefixAttribute>() != null);
        foreach (var type in prefixFields)
        {
            if (ignoredTypes?.Contains(type) == true)
            {
                continue;
            }

            var attribute = type.GetCustomAttribute<NetFieldExportGroupPrefixAttribute>();
            if (attribute.MinimalParseMode <= mode)
            {
                var info = new NetFieldGroupInfo
                {
                    Type = type,
                    TypeId = _linqCache.AddExportType(type),
                };

                AddNetFieldInfo(type, info, mode);
                _prefixGroups.Add((attribute.Prefix, info));
            }
        }

        // Add all subgroups
        var netSubFields = types.Where(c => c.GetCustomAttribute<NetFieldExportSubGroupAttribute>() != null);
        foreach (var type in netSubFields)
        {
            var attribute = type.GetCustomAttribute<NetFieldExportSubGroupAttribute>();
            if (attribute.MinimalParseMode <= mode && NetFieldGroups.TryGetValue(attribute.Path, out var info))
            {
                AddNetFieldInfo(type, info, mode);
            }
        }

        // ClassNetCaches
        var classNetCaches = types.Where(c => c.GetCustomAttribute<NetFieldExportClassNetCacheAttribute>() != null);
        foreach (var type in classNetCaches)
        {
            var attribute = type.GetCustomAttribute<NetFieldExportClassNetCacheAttribute>();
            if (attribute.MinimalParseMode <= mode)
            {
                var info = new ClassNetCacheInfo();
                AddClassNetInfo(type, info);
                _classNetCacheToNetFieldGroup[attribute.Path] = info;
            }
        }

        // PlayerControllers
        var controllers = types.Where(c => c.GetCustomAttribute<PlayerControllerAttribute>() != null);
        foreach (var type in controllers)
        {
            var attribute = type.GetCustomAttribute<PlayerControllerAttribute>();
            PlayerControllerGroups.Add(attribute.Path);
        }

        // Type layout for dynamic arrays
        _primitiveTypeLayout.Add(typeof(bool), RepLayoutCmdType.PropertyBool);
        _primitiveTypeLayout.Add(typeof(byte), RepLayoutCmdType.PropertyByte);
        _primitiveTypeLayout.Add(typeof(ushort), RepLayoutCmdType.PropertyUInt16);
        _primitiveTypeLayout.Add(typeof(int), RepLayoutCmdType.PropertyInt);
        _primitiveTypeLayout.Add(typeof(uint), RepLayoutCmdType.PropertyUInt32);
        _primitiveTypeLayout.Add(typeof(ulong), RepLayoutCmdType.PropertyUInt64);
        _primitiveTypeLayout.Add(typeof(float), RepLayoutCmdType.PropertyFloat);
        _primitiveTypeLayout.Add(typeof(string), RepLayoutCmdType.PropertyString);
        _primitiveTypeLayout.Add(typeof(object), RepLayoutCmdType.Ignore);

        // Allows deserializing type arrays
        var iPropertyTypes = types.Where(x => typeof(IProperty).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract);
        foreach (var iPropertyType in iPropertyTypes)
        {
            _primitiveTypeLayout.Add(iPropertyType, RepLayoutCmdType.Property);
        }

        //Load object instances
        _objects = new SingleInstanceExport[_linqCache.TotalTypes];

        IEnumerable<NetFieldGroupInfo> allGroups = Enumerable.Range(0, NetFieldGroups.Length).Select(i => NetFieldGroups[i]).Concat(_prefixGroups.Select(p => p.Info));
        foreach (var group in allGroups)
        {
            _objects[group.TypeId] = new SingleInstanceExport
            {
                Instance = (INetFieldExportGroup)Activator.CreateInstance(group.Type),
                ChangedProperties = new List<NetFieldInfo>(group.Properties.Length),
            };
        }
    }

    /// <summary>
    /// Adds <paramref name="group"/> as an alias of the first prefix group it starts with.
    /// </summary>
    /// <returns>true if a prefix group matched</returns>
    private bool TryAddPrefixAlias(string group)
    {
        if (_prefixGroups.Count == 0 || string.IsNullOrEmpty(group) || _noPrefixMatch.Contains(group))
        {
            return false;
        }

        foreach ((string prefix, NetFieldGroupInfo info) in _prefixGroups)
        {
            if (group.StartsWith(prefix, StringComparison.Ordinal))
            {
                NetFieldGroups.Add(group, info);
                return true;
            }
        }

        _noPrefixMatch.Add(group);
        return false;
    }

    private bool TryGetGroup(string group, [NotNullWhen(returnValue: true)] out NetFieldGroupInfo? info)
    {
        if (NetFieldGroups.TryGetValue(group, out info))
        {
            return true;
        }

        return TryAddPrefixAlias(group) && NetFieldGroups.TryGetValue(group, out info);
    }

    /// <summary>
    /// https://codeblog.jonskeet.uk/2008/08/09/making-reflection-fly-and-exploring-delegates/
    /// </summary>
    /// <summary>
    /// The read method of a plain value per command type: the same methods <see cref="ReadDataType"/> uses. Command types that are
    /// not in here (structs that implement <see cref="IProperty"/>, arrays, movement) keep the general path.
    /// </summary>
    private static readonly Dictionary<RepLayoutCmdType, (string Method, object[] Arguments)> TypedReadMethods = new()
    {
        [RepLayoutCmdType.PropertyBool] = (nameof(NetBitReader.SerializePropertyBool), []),
        [RepLayoutCmdType.PropertyName] = (nameof(NetBitReader.SerializePropertyName), []),
        [RepLayoutCmdType.PropertyFloat] = (nameof(NetBitReader.SerializePropertyFloat), []),
        [RepLayoutCmdType.PropertyDouble] = (nameof(NetBitReader.SerializePropertyDouble), []),
        [RepLayoutCmdType.PropertyNativeBool] = (nameof(NetBitReader.SerializePropertyNativeBool), []),
        [RepLayoutCmdType.PropertyNetId] = (nameof(NetBitReader.SerializePropertyNetId), []),
        [RepLayoutCmdType.PropertyObject] = (nameof(NetBitReader.SerializePropertyObject), []),
        [RepLayoutCmdType.PropertyRotator] = (nameof(NetBitReader.SerializePropertyRotator), []),
        [RepLayoutCmdType.PropertyString] = (nameof(NetBitReader.SerializePropertyString), []),
        [RepLayoutCmdType.PropertyVector10] = (nameof(NetBitReader.SerializePropertyVector10), []),
        [RepLayoutCmdType.PropertyVector100] = (nameof(NetBitReader.SerializePropertyVector100), []),
        [RepLayoutCmdType.PropertyVectorNormal] = (nameof(NetBitReader.SerializePropertyVectorNormal), []),
        [RepLayoutCmdType.PropertyVectorQ] = (nameof(NetBitReader.SerializePropertyQuantizedVector), [VectorQuantization.RoundWholeNumber]),
        [RepLayoutCmdType.Enum] = (nameof(NetBitReader.SerializePropertyEnum), []),
        [RepLayoutCmdType.PropertyByte] = (nameof(NetBitReader.ReadByte), []),
        [RepLayoutCmdType.PropertyInt] = (nameof(NetBitReader.ReadInt32), []),
        [RepLayoutCmdType.PropertyInt16] = (nameof(NetBitReader.ReadInt16), []),
        [RepLayoutCmdType.PropertyUInt64] = (nameof(NetBitReader.ReadUInt64), []),
        [RepLayoutCmdType.PropertyUInt16] = (nameof(NetBitReader.ReadUInt16), []),
        [RepLayoutCmdType.PropertyUInt32] = (nameof(NetBitReader.ReadUInt32), []),
        [RepLayoutCmdType.PropertyVector] = (nameof(NetBitReader.SerializePropertyVector), []),
        [RepLayoutCmdType.PropertyVector2D] = (nameof(NetBitReader.SerializePropertyVector2D), []),
    };

    /// <summary>
    /// Compiles <c>(group, reader) =&gt; { var value = reader.ReadX(); if (reader.IsError || value is null) return false; group.field = value; return true; }</c>
    /// for a plain value. The general path reads the value as <see cref="object"/> (a value type gets boxed), finds the read
    /// method in a <c>switch</c> and stores it through a setter that unboxes it again; this does the same without boxing.
    /// <c>null</c> if the command type has no plain read method or the value does not fit the property.
    /// </summary>
    // Compiled code depends only on the property and its command type, so it is shared by all parsers (every replay creates
    // its own parser; compiling the same delegates again for every replay cost more than it saved).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(PropertyInfo, RepLayoutCmdType), Func<INetFieldExportGroup, NetBitReader, bool>?> TypedReads = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(PropertyInfo, object?), Action<INetFieldExportGroup>> TypedResets = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<PropertyInfo, Action<INetFieldExportGroup, object>> Setters = new();

    private static Func<INetFieldExportGroup, NetBitReader, bool>? BuildTypedRead(NetFieldInfo info) =>
        info.ElementTypeId is not null ? null : TypedReads.GetOrAdd((info.PropertyInfo, info.Attribute.Type), _ => CompileTypedRead(info));

    private static Func<INetFieldExportGroup, NetBitReader, bool>? CompileTypedRead(NetFieldInfo info)
    {
        if (info.ElementTypeId is not null || !TypedReadMethods.TryGetValue(info.Attribute.Type, out var read) || BackingField(info.PropertyInfo) is not { } field)
        {
            return null;
        }

        try
        {
            var method = typeof(NetBitReader).GetMethod(read.Method, [.. read.Arguments.Select(a => a.GetType())]);
            if (method is null)
            {
                return null;
            }

            var group = Expression.Parameter(typeof(INetFieldExportGroup), "group");
            var reader = Expression.Parameter(typeof(NetBitReader), "reader");
            var value = Expression.Variable(method.ReturnType, "value");
            var done = Expression.Label(typeof(bool));

            // Like the general path: a failed read or a null value leaves the property as it is.
            Expression failed = Expression.Property(reader, nameof(NetBitReader.IsError));
            if (!method.ReturnType.IsValueType)
            {
                failed = Expression.OrElse(failed, Expression.Equal(value, Expression.Constant(null, method.ReturnType)));
            }

            var body = Expression.Block(
                [value],
                Expression.Assign(value, Expression.Call(reader, method, read.Arguments.Select(a => (Expression)Expression.Constant(a)))),
                Expression.IfThen(failed, Expression.Return(done, Expression.Constant(false))),
                Expression.Assign(Expression.Field(Expression.Convert(group, field.DeclaringType!), field), Expression.Convert(value, field.FieldType)),
                Expression.Label(done, Expression.Constant(true)));

            return Expression.Lambda<Func<INetFieldExportGroup, NetBitReader, bool>>(body, group, reader).Compile();
        }
        catch (InvalidOperationException)
        {
            // The read type cannot be converted to the property type: keep the general path (and its behavior).
            return null;
        }
    }

    /// <summary>Compiles <c>group =&gt; group.field = DefaultValue</c> (no boxing), or falls back to the general setter.</summary>
    private static Action<INetFieldExportGroup> BuildTypedReset(NetFieldInfo info) =>
        TypedResets.GetOrAdd((info.PropertyInfo, info.DefaultValue), _ => CompileTypedReset(info));

    private static Action<INetFieldExportGroup> CompileTypedReset(NetFieldInfo info)
    {
        if (BackingField(info.PropertyInfo) is { } field)
        {
            var group = Expression.Parameter(typeof(INetFieldExportGroup), "group");
            var reset = Expression.Assign(Expression.Field(Expression.Convert(group, field.DeclaringType!), field), Expression.Constant(info.DefaultValue, field.FieldType));
            return Expression.Lambda<Action<INetFieldExportGroup>>(reset, group).Compile();
        }

        info.SetMethod ??= CreateSetter(info.PropertyInfo);
        return instance => info.SetMethod(instance, info.DefaultValue);
    }

    private static FieldInfo? BackingField(PropertyInfo property) =>
        property.DeclaringType?.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Action<INetFieldExportGroup, object> CreateSetter(PropertyInfo propertyInfo) => Setters.GetOrAdd(propertyInfo, CompileSetter);

    private static Action<INetFieldExportGroup, object> CompileSetter(PropertyInfo propertyInfo)
    {
        var field = GetBackingField(propertyInfo);

        var methodName = field.ReflectedType.FullName + ".set_" + field.Name;
        var setterMethod = new DynamicMethod(methodName, null, new Type[2] { typeof(INetFieldExportGroup), typeof(object) }, true);
        var gen = setterMethod.GetILGenerator();
        if (field.IsStatic)
        {
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Stsfld, field);
        }
        else
        {
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldarg_1);
            gen.Emit(OpCodes.Unbox_Any, field.FieldType);
            gen.Emit(OpCodes.Stfld, field);
        }
        gen.Emit(OpCodes.Ret);

        return (Action<INetFieldExportGroup, object>)setterMethod.CreateDelegate(typeof(Action<INetFieldExportGroup, object>));

        FieldInfo GetBackingField(PropertyInfo property)
        {
            return property.DeclaringType.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }

    private void AddNetFieldInfo(Type type, NetFieldGroupInfo info, ParseMode mode)
    {
        IReadOnlySet<string>? only = _onlyProperties?.GetValueOrDefault(type);
        foreach (var property in type.GetProperties())
        {
            // Not wanted: the export finds no property, so ReceiveProperties skips its bits from then on.
            if (only is not null && !only.Contains(property.Name))
            {
                continue;
            }

            // A property can be exported under several names (the same data has a different name in different groups or versions).
            var allExportAttributes = property.GetCustomAttributes<NetFieldExportAttribute>().ToList();
            var netFieldExportAttributes = allExportAttributes
                .Where(a => a.MinimalParseMode == null || a.MinimalParseMode <= mode)
                .ToList();
            var handleAttribute = property.GetCustomAttribute<NetFieldExportHandleAttribute>();

            if (allExportAttributes.Count == 0 && handleAttribute == null)
            {
                continue;
            }

            if (allExportAttributes.Count > 0 && netFieldExportAttributes.Count == 0)
            {
                continue;
            }

            if (handleAttribute != null && handleAttribute.MinimalParseMode != null && handleAttribute.MinimalParseMode > mode)
            {
                continue;
            }

            int? elementTypeId = null;
            if (property.PropertyType.IsArray)
            {
                var elementType = property.PropertyType.GetElementType();

                if (typeof(INetFieldExportGroup).IsAssignableFrom(elementType))
                {
                    elementTypeId = _linqCache.AddExportType(elementType);
                }
            }

            NetFieldInfo CreateInfo(NetFieldAttribute attribute)
            {
                var netFieldInfo = new NetFieldInfo
                {
                    MovementAttribute = property.GetCustomAttribute<RepMovementAttribute>(),
                    PropertyInfo = property,
                    ElementTypeId = elementTypeId,
                    Attribute = attribute,
                };

                if (property.PropertyType.IsEnum)
                {
                    netFieldInfo.DefaultValue = Enum.GetValues(property.PropertyType).Cast<int>().Max();
                }
                else if (property.PropertyType.IsValueType)
                {
                    netFieldInfo.DefaultValue = Activator.CreateInstance(property.PropertyType);
                }

                return netFieldInfo;
            }

            if (netFieldExportAttributes.Count > 0)
            {
                foreach (var netFieldExportAttribute in netFieldExportAttributes)
                {
                    info.Properties.Add(PropertyKey(netFieldExportAttribute.Name, netFieldExportAttribute.Occurrence), CreateInfo(netFieldExportAttribute));
                }
            }
            else
            {
                info.UsesHandles = true;
                info.Handles.Add(handleAttribute.Handle, CreateInfo(handleAttribute));
            }
        }
    }

    private void AddClassNetInfo(Type type, ClassNetCacheInfo info)
    {
        foreach (var property in type.GetProperties())
        {
            var structAttribute = property.GetCustomAttribute<NetFieldExportRPCAttribute>();
            if (structAttribute != null)
            {
                info.Properties[structAttribute.Name] = new ClassNetCachePropertyInfo()
                {
                    PropertyInfo = property,
                    Name = structAttribute.Name,
                    PathName = structAttribute.PathName,
                    IsFunction = structAttribute.IsFunction,
                    EnablePropertyChecksum = structAttribute.EnablePropertyChecksum,
                    IsCustomStruct = structAttribute.CustomStruct
                };
            }
        }
    }

    /// <summary>
    /// Returns whether or not this <paramref name="group"/> is marked to be parsed.
    /// </summary>
    /// <param name="group"></param>
    /// <returns>true if group should be parsed further, false otherwise</returns>
    public bool WillReadType(string group) => TryGetGroup(group, out var _);

    /// <summary>
    /// Returns whether or not this <paramref name="group"/> is marked to be parsed.
    /// </summary>
    /// <param name="group"></param>
    /// <returns>true if group should be parsed further, false otherwise</returns>
    public bool WillReadClassNetCache(string group) => _classNetCacheToNetFieldGroup.ContainsKey(group);

    /// <summary>
    /// Returns whether or not the property of this classnetcache was found.
    /// </summary>
    /// <param name="group"></param>
    /// <returns>true if classnetcache property was found, false otherwise</returns>
    public bool TryGetClassNetCacheProperty(string property, string group, [NotNullWhen(returnValue: true)] out ClassNetCachePropertyInfo? info)
    {
        info = null;
        if (_classNetCacheToNetFieldGroup.TryGetValue(group, out var groupInfo))
        {
            return groupInfo.Properties.TryGetValue(property, out info);
        }
        return false;
    }

    /// <summary>
    /// Tries to read the property and update the value accordingly.
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="export"></param>
    /// <param name="handle"></param>
    /// <param name="exportGroup"></param>
    /// <param name="netBitReader"></param>
    public bool ReadField(INetFieldExportGroup obj, NetFieldExport export, uint handle, NetFieldExportGroup exportGroup, NetBitReader netBitReader, bool singleInstance = true)
    {
        if (export.PropertyId == -2 || exportGroup.GroupId == -2)
        {
            return false;
        }

        if (exportGroup.GroupId == -1)
        {
            if (!NetFieldGroups.TryGetIndex(exportGroup.PathName, out var groupIndex) &&
                !(TryAddPrefixAlias(exportGroup.PathName) && NetFieldGroups.TryGetIndex(exportGroup.PathName, out groupIndex)))
            {
                exportGroup.GroupId = -2;
                return false;
            }
            exportGroup.GroupId = groupIndex;
        }

        var netGroupInfo = NetFieldGroups[exportGroup.GroupId];
        NetFieldInfo netFieldInfo;
        if (netGroupInfo.UsesHandles)
        {
            if (!netGroupInfo.Handles.TryGetValue(handle, out netFieldInfo))
            {
                return false;
            }
        }
        else
        {
            var propertyIndex = export.PropertyId;
            if (export.PropertyId == -1)
            {
                // A property bound to the n-th export of this name wins over one that takes all of them.
                if (!netGroupInfo.Properties.TryGetIndex(PropertyKey(export.Name, Occurrence(exportGroup, export, handle)), out propertyIndex) &&
                    !netGroupInfo.Properties.TryGetIndex(export.Name, out propertyIndex))
                {
                    export.PropertyId = -2;
                    return false;
                }

                export.PropertyId = propertyIndex;
            }

            netFieldInfo = netGroupInfo.Properties[propertyIndex];
        }

        SetType(obj, netFieldInfo, netGroupInfo, exportGroup, netBitReader, singleInstance);
        return true;
    }

    /// <summary>Key of a property in <see cref="NetFieldGroupInfo.Properties"/>, see <see cref="NetFieldExportAttribute.Occurrence"/>.</summary>
    private static string PropertyKey(string name, int occurrence) => occurrence < 0 ? name : $"{name}#{occurrence}";

    /// <summary>How many exports with the same name as <paramref name="export"/> the group has below <paramref name="handle"/>.</summary>
    private static int Occurrence(NetFieldExportGroup group, NetFieldExport export, uint handle)
    {
        var occurrence = 0;
        for (var i = 0; i < handle && i < group.NetFieldExports.Length; i++)
        {
            if (group.NetFieldExports[i]?.Name == export.Name)
            {
                occurrence++;
            }
        }

        return occurrence;
    }

    private void SetType(INetFieldExportGroup obj, NetFieldInfo netFieldInfo, NetFieldGroupInfo groupInfo, NetFieldExportGroup exportGroup, NetBitReader netBitReader, bool singleInstance)
    {
        if (!netFieldInfo.TypedReadBuilt)
        {
            netFieldInfo.TypedRead = BuildTypedRead(netFieldInfo);
            netFieldInfo.TypedReadBuilt = true;
        }

        if (netFieldInfo.TypedRead is { } typedRead)
        {
            if (typedRead(obj, netBitReader) && singleInstance)
            {
                _objects[groupInfo.TypeId].ChangedProperties.Add(netFieldInfo);
            }

            return;
        }

        var data = netFieldInfo.Attribute.Type switch
        {
            RepLayoutCmdType.DynamicArray => ReadArrayField(exportGroup, netFieldInfo, groupInfo, netBitReader),
            RepLayoutCmdType.RepMovement => netFieldInfo.MovementAttribute != null ? netBitReader.SerializeRepMovement(
                locationQuantizationLevel: netFieldInfo.MovementAttribute.LocationQuantizationLevel,
                rotationQuantizationLevel: netFieldInfo.MovementAttribute.RotationQuantizationLevel,
                velocityQuantizationLevel: netFieldInfo.MovementAttribute.VelocityQuantizationLevel)
                // TODO: support custom FEngineNetworkCustomVersion::Guid
                : netBitReader.SerializeRepMovement(rotationQuantizationLevel: RepMovementRotation(netBitReader.NetworkReplayVersion)),
            _ => ReadDataType(netFieldInfo.Attribute.Type, netBitReader, netFieldInfo.PropertyInfo.PropertyType),
        };

        if (data != null && !netBitReader.IsError)
        {
            if (singleInstance)
            {
                _objects[groupInfo.TypeId].ChangedProperties.Add(netFieldInfo);
            }

            if (netFieldInfo.SetMethod == null)
            {
                netFieldInfo.SetMethod = CreateSetter(netFieldInfo.PropertyInfo);
            }

            netFieldInfo.SetMethod(obj, data);
        }
    }

    private NetworkReplayVersion? _repMovementVersion;
    private RotatorQuantization _repMovementRotation;

    /// <summary>
    /// Rotation format of a replicated movement for the replay version. The version is the same for a whole replay, so the
    /// answer (a string search in the branch name) is computed once instead of for every movement update.
    /// </summary>
    private RotatorQuantization RepMovementRotation(NetworkReplayVersion? version)
    {
        if (!ReferenceEquals(version, _repMovementVersion) || version is null)
        {
            _repMovementVersion = version;
            _repMovementRotation = version != null && (version.Changelist >= 54618515u || (version.Branch?.Contains("+Release-41.") ?? false))
                ? RotatorQuantization.ShortComponents
                : RotatorQuantization.ByteComponents;
        }

        return _repMovementRotation;
    }

    private object? ReadDataType(RepLayoutCmdType replayout, NetBitReader netBitReader, Type objectType)
    {
        object? data = null;

        switch (replayout)
        {
            case RepLayoutCmdType.Property:
                data = _linqCache.CreatePropertyObject(objectType);
                (data as IProperty)?.Serialize(netBitReader);
                (data as IResolvable)?.Resolve(GuidCache);
                break;
            case RepLayoutCmdType.PropertyBool:
                data = netBitReader.SerializePropertyBool();
                break;
            case RepLayoutCmdType.PropertyName:
                data = netBitReader.SerializePropertyName();
                break;
            case RepLayoutCmdType.PropertyFloat:
                data = netBitReader.SerializePropertyFloat();
                break;
            case RepLayoutCmdType.PropertyDouble:
                data = netBitReader.SerializePropertyDouble();
                break;
            case RepLayoutCmdType.PropertyNativeBool:
                data = netBitReader.SerializePropertyNativeBool();
                break;
            case RepLayoutCmdType.PropertyNetId:
                data = netBitReader.SerializePropertyNetId();
                break;
            case RepLayoutCmdType.PropertyObject:
                data = netBitReader.SerializePropertyObject();
                break;
            case RepLayoutCmdType.PropertyPlane:
                throw new NotImplementedException("Plane RepLayoutCmdType not implemented");
            case RepLayoutCmdType.PropertyRotator:
                data = netBitReader.SerializePropertyRotator();
                break;
            case RepLayoutCmdType.PropertyString:
                data = netBitReader.SerializePropertyString();
                break;
            case RepLayoutCmdType.PropertyVector10:
                data = netBitReader.SerializePropertyVector10();
                break;
            case RepLayoutCmdType.PropertyVector100:
                data = netBitReader.SerializePropertyVector100();
                break;
            case RepLayoutCmdType.PropertyVectorNormal:
                data = netBitReader.SerializePropertyVectorNormal();
                break;
            case RepLayoutCmdType.PropertyVectorQ:
                data = netBitReader.SerializePropertyQuantizedVector(VectorQuantization.RoundWholeNumber);
                break;
            case RepLayoutCmdType.RepMovement:
                data = netBitReader.SerializeRepMovement();
                break;
            case RepLayoutCmdType.Enum:
                data = netBitReader.SerializePropertyEnum();
                break;
            // TODO Auto generation fix to handle 1-8 bits
            case RepLayoutCmdType.PropertyByte:
                data = netBitReader.ReadByte();
                break;
            case RepLayoutCmdType.PropertyInt:
                data = netBitReader.ReadInt32();
                break;
            case RepLayoutCmdType.PropertyInt16:
                data = netBitReader.ReadInt16();
                break;
            case RepLayoutCmdType.PropertyUInt64:
                data = netBitReader.ReadUInt64();
                break;
            case RepLayoutCmdType.PropertyUInt16:
                data = netBitReader.ReadUInt16();
                break;
            case RepLayoutCmdType.PropertyUInt32:
                data = netBitReader.ReadUInt32();
                break;
            case RepLayoutCmdType.PropertyVector:
                data = netBitReader.SerializePropertyVector();
                break;
            case RepLayoutCmdType.PropertyVector2D:
                data = netBitReader.SerializePropertyVector2D();
                break;
            default:
                netBitReader.Seek(netBitReader.GetBitsLeft(), System.IO.SeekOrigin.Current);
                break;
        }

        return data;
    }

    /// <summary>
    /// see https://github.com/EpicGames/UnrealEngine/blob/5677c544747daa1efc3b5ede31642176644518a6/Engine/Source/Runtime/Engine/Private/RepLayout.cpp#L3141
    /// </summary>
    private Array ReadArrayField(NetFieldExportGroup netfieldExportGroup, NetFieldInfo fieldInfo, NetFieldGroupInfo groupInfo, NetBitReader netBitReader)
    {
        var arrayIndexes = netBitReader.ReadIntPacked();

        var elementType = fieldInfo.PropertyInfo.PropertyType.GetElementType();
        var replayout = RepLayoutCmdType.Ignore;
        var isGroupType = elementType == groupInfo.Type || elementType == groupInfo.Type.BaseType;

        if (!isGroupType)
        {
            groupInfo = null;

            if (!_primitiveTypeLayout.TryGetValue(elementType, out replayout))
            {
                replayout = RepLayoutCmdType.Ignore;
            }
        }

        // Elements are either the group type itself (their fields are exports of the same group) or a primitive type.
        if (isGroupType ? fieldInfo.ElementTypeId is null : replayout == RepLayoutCmdType.Ignore)
        {
            return null;
        }

        var arr = Array.CreateInstance(elementType, arrayIndexes);

        while (true)
        {
            var index = netBitReader.ReadIntPacked();

            if (index == 0)
            {
                // At this point, the 0 either signifies:
                // An array terminator, at which point we're done.
                // An array element terminator, which could happen if the array had tailing elements removed.
                if (netBitReader.GetBitsLeft() == 8)
                {
                    // We have bits left over, so see if its the Array Terminator.
                    // This should be 0
                    var terminator = netBitReader.ReadIntPacked();

                    if (terminator != 0x00)
                    {
                        //Log error

                        return arr;
                    }
                }

                return arr;
            }

            // Shift all indexes down since 0 represents null handle
            index--;

            if (index >= arrayIndexes)
            {
                //Log error
                return arr;
            }

            object? data = null;

            if (isGroupType)
            {
                data = _linqCache.CreateObject(fieldInfo.ElementTypeId.Value);
            }

            while (true)
            {
                var handle = netBitReader.ReadIntPacked();

                if (handle == 0)
                {
                    break;
                }

                handle--;

                if (netfieldExportGroup.NetFieldExportsLength < handle)
                {
                    return arr;
                }

                var export = netfieldExportGroup.NetFieldExports[handle];
                var numBits = netBitReader.ReadIntPacked();

                if (numBits == 0)
                {
                    continue;
                }

                if (export == null)
                {
                    netBitReader.SkipBits((int)numBits);
                    continue;
                }

                try
                {
                    netBitReader.SetTempEnd((int)numBits, FBitArchiveEndIndex.READ_ARRAY_FIELD);

                    if (groupInfo != null)
                    {
                        ReadField((INetFieldExportGroup)data, export, handle, netfieldExportGroup, netBitReader, singleInstance: false);
                    }
                    else
                    {
                        data = ReadDataType(replayout, netBitReader, elementType);
                    }
                }
                finally
                {
                    netBitReader.RestoreTempEnd(FBitArchiveEndIndex.READ_ARRAY_FIELD);
                }
            }

            arr.SetValue(data, index);
        }
    }

    /// <summary>
    /// Create the object associated with the NetFieldExportGroup.
    /// </summary>
    /// <param name="group"></param>
    /// <returns></returns>
    public INetFieldExportGroup? CreateType(string group)
    {
        if (!TryGetGroup(group, out var exportGroup))
        {
            return null;
        }

        return ResetInstance(exportGroup);
    }

    /// <summary>
    /// Same as <see cref="CreateType(string)"/>, but finds the group by the index cached in <paramref name="group"/> instead of
    /// looking up its path (a string hash of the long path for every property update).
    /// </summary>
    public INetFieldExportGroup? CreateType(NetFieldExportGroup group)
    {
        int index = GroupIndex(group);
        return index < 0 ? null : ResetInstance(NetFieldGroups[index]);
    }

    /// <summary>Same as <see cref="WillReadType(string)"/> with the index cached in <paramref name="group"/>.</summary>
    public bool WillReadType(NetFieldExportGroup group) => GroupIndex(group) >= 0;

    /// <summary>Index of the group in <see cref="NetFieldGroups"/>, looked up once per group and kept in it; -2 if it is not read.</summary>
    private int GroupIndex(NetFieldExportGroup group)
    {
        if (group.GroupId == -1)
        {
            group.GroupId = NetFieldGroups.TryGetIndex(group.PathName, out var index) ||
                (TryAddPrefixAlias(group.PathName) && NetFieldGroups.TryGetIndex(group.PathName, out index))
                ? index
                : -2;
        }

        return group.GroupId;
    }

    private INetFieldExportGroup ResetInstance(NetFieldGroupInfo exportGroup)
    {
        var cachedEntry = _objects[exportGroup.TypeId];

        for (var i = 0; i < cachedEntry.ChangedProperties.Count; i++)
        {
            var fieldInfo = cachedEntry.ChangedProperties[i];
            (fieldInfo.TypedReset ??= BuildTypedReset(fieldInfo))(cachedEntry.Instance);
        }

        cachedEntry.ChangedProperties.Clear();
        return cachedEntry.Instance;
    }

    /// <summary>
    /// Create the object associated with the property that should be read.
    /// Used as a workaround for RPC structs.
    /// </summary>
    public IProperty? CreatePropertyType(string group, string propertyName)
    {
        if (_classNetCacheToNetFieldGroup.TryGetValue(group, out var groupInfo))
        {
            if (groupInfo.Properties.TryGetValue(propertyName, out var fieldInfo))
            {
                return _linqCache.CreatePropertyObject(fieldInfo.PropertyInfo.PropertyType);
            }
        }
        return null;
    }

    private sealed class NetFieldGroupInfo
    {
        public Type Type { get; set; }
        public int TypeId { get; set; }
        public bool UsesHandles { get; set; }
        public KeyList<string, NetFieldInfo> Properties { get; set; } = new();
        public Dictionary<uint, NetFieldInfo> Handles { get; set; } = new();
    }

    private sealed class NetFieldInfo
    {
        public RepMovementAttribute? MovementAttribute { get; set; }
        public NetFieldAttribute Attribute { get; set; }
        public PropertyInfo PropertyInfo { get; set; }
        public int? ElementTypeId { get; set; }
        public object DefaultValue { get; set; }
        public Action<INetFieldExportGroup, object> SetMethod { get; set; }

        /// <summary>Reads the value with its typed read method and stores it, see <see cref="BuildTypedRead"/>. <c>null</c>: use the general path.</summary>
        public Func<INetFieldExportGroup, NetBitReader, bool>? TypedRead { get; set; }
        public bool TypedReadBuilt { get; set; }

        /// <summary>Puts <see cref="DefaultValue"/> back without boxing, see <see cref="BuildTypedReset"/>.</summary>
        public Action<INetFieldExportGroup>? TypedReset { get; set; }
    }

    // TODO make private
    public sealed class ClassNetCachePropertyInfo
    {
        public PropertyInfo PropertyInfo { get; set; }
        public string Name { get; set; }
        public string PathName { get; set; }
        public bool EnablePropertyChecksum { get; set; }
        public bool IsFunction { get; set; }
        public bool IsCustomStruct { get; set; }
    }

    private sealed class ClassNetCacheInfo
    {
        public Dictionary<string, ClassNetCachePropertyInfo> Properties { get; set; } = new();
    }

    private sealed class SingleInstanceExport
    {
        //Single instance
        internal INetFieldExportGroup Instance { get; set; }
        internal List<NetFieldInfo> ChangedProperties { get; set; }
    }
}
