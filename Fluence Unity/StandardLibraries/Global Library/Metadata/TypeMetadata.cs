using System;
using System.Collections.Generic;
using System.Linq;

namespace Fluence.Unity.Global
{
    /// <summary>
    /// An immutable record holding metadata about a single struct field.
    /// </summary>
    internal sealed class FieldMetadata
    {
        internal string Name;
        internal bool IsStatic;
        internal bool IsSolid;

        public FieldMetadata(string name, bool isStatic, bool isSolid)
        {
            Name = name;
            IsStatic = isStatic;
            IsSolid = isSolid;
        }
    }

    internal sealed class TypeMetadata
    {
        /// <summary> The user-facing name of the type. </summary>
        internal string Name { get; }

        /// <summary>
        /// The full name of the type, including its namespace.
        /// For global types, this is the same as Name.
        /// </summary>
        internal string FullName { get; }

        /// <summary>
        /// The arity of the type, if not a function or lambda it is 0.
        /// </summary>
        internal int Arity { get; }

        /// <summary>
        /// Indicates whether the type is that of a lambda.
        /// </summary>
        internal bool IsLambda { get; }

        /// <summary>
        /// The category of the type.
        /// </summary>
        internal TypeCategory Category { get; }

        /// <summary>
        /// A list of metadata for each instance field defined on the struct.
        /// Returns an empty list for non-struct types.
        /// </summary>
        internal IReadOnlyList<FieldMetadata> InstanceFields { get; }

        /// <summary>
        /// The names of the non static solid fields of the struct.
        /// </summary>
        internal HashSet<string> InstanceFieldNames { get; }

        /// <summary>
        /// A list of metadata for each static or solid field defined on the struct.
        /// Returns an empty list for non-struct types.
        /// </summary>
        internal IReadOnlyList<FieldMetadata> StaticFields { get; }

        /// <summary>
        /// The static solid fields of a struct, if the type is a struct.
        /// </summary>
        internal HashSet<string> StaticFieldNames { get; }

        /// <summary>
        /// The parameters of the function, if the type is that of a lambda or a function.
        /// </summary>
        internal HashSet<string> Parameters { get; }

        /// <summary>
        /// A bitmask identifying which arguments are passed by reference. 
        /// If the bit at position 'i' is set, the argument at index 'i' is passed by reference.
        /// Limits the function to a maximum of 32 arguments ( more than reasonalbe ).
        /// </summary>
        internal int RefMask { get; }

        /// <summary>
        /// The struct symbol of the type, if the type is that of a struct.
        /// </summary>
        internal StructSymbol StructSymbol { get; }

        /// <summary>
        /// A list of metadata for each constructor defined on the struct.
        /// </summary>
        public IReadOnlyList<MethodMetadata> Constructors { get; }

        /// <summary>
        /// A list of metadata for each instance method defined on the struct.
        /// </summary>
        internal IReadOnlyList<MethodMetadata> InstanceMethods { get; }

        /// <summary>
        /// A list of the names of all members of an enum.
        /// Returns an empty list for non-enum types.
        /// </summary>
        internal IReadOnlyList<string> EnumMembers { get; }

        internal TypeMetadata(
            string name,
            string fullName,
            TypeCategory category,
            int arity,
            bool isLambda,
            IReadOnlyList<FieldMetadata>? instanceFields = null,
            IReadOnlyList<FieldMetadata>? staticFields = null,
            IReadOnlyList<MethodMetadata>? constructors = null,
            IReadOnlyList<MethodMetadata>? instanceMethods = null,
            IReadOnlyList<string>? enumMembers = null,
            IReadOnlyList<string>? parameters = null,
            int refMask = 0,
            StructSymbol symbol = null!)
        {
            Name = name;
            FullName = fullName;
            Category = category;
            Arity = arity;
            IsLambda = isLambda;
            InstanceFields = instanceFields ?? new List<FieldMetadata>();
            StaticFields = staticFields ?? new List<FieldMetadata>();
            Constructors = constructors ?? new List<MethodMetadata>();
            InstanceMethods = instanceMethods ?? new List<MethodMetadata>();
            EnumMembers = enumMembers ?? new List<string>();
            Parameters = parameters is not null ? new HashSet<string>(parameters) : new HashSet<string>();
            RefMask = refMask;

            InstanceFieldNames = new HashSet<string>(InstanceFields.Select(x => x.Name));
            StaticFieldNames = new HashSet<string>(StaticFields.Select(x => x.Name));
            StructSymbol = symbol;
        }

        public bool Equals(TypeMetadata? other)
        {
            if (other is null) return false;

            if (ReferenceEquals(this, other)) return true;

            if (Name != other.Name || FullName != other.FullName || Category != other.Category || Arity != other.Arity || IsLambda != other.IsLambda)
            {
                return false;
            }

            if (!EnumMembers.SequenceEqual(other.EnumMembers)) return false;

            if (!new HashSet<FieldMetadata>(InstanceFields).SetEquals(other.InstanceFields)) return false;
            if (!new HashSet<FieldMetadata>(StaticFields).SetEquals(other.StaticFields)) return false;
            if (!new HashSet<MethodMetadata>(Constructors).SetEquals(other.Constructors)) return false;
            if (!new HashSet<MethodMetadata>(InstanceMethods).SetEquals(other.InstanceMethods)) return false;
            if (!Parameters.SetEquals(other.Parameters)) return false;
            if (RefMask != other.RefMask) return false;

            // If all checks pass, the objects are identical.
            return true;
        }

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Name);
            hash.Add(FullName);
            hash.Add(Category);
            hash.Add(Arity);
            hash.Add(IsLambda);

            int instanceFieldsHash = 0;
            foreach (FieldMetadata item in InstanceFields) { instanceFieldsHash ^= item.GetHashCode(); }
            hash.Add(instanceFieldsHash);

            int staticFieldsHash = 0;
            foreach (FieldMetadata item in StaticFields) { staticFieldsHash ^= item.GetHashCode(); }
            hash.Add(staticFieldsHash);

            int constructorsHash = 0;
            foreach (MethodMetadata item in Constructors) { constructorsHash ^= item.GetHashCode(); }
            hash.Add(constructorsHash);

            int instanceMethodsHash = 0;
            foreach (MethodMetadata item in InstanceMethods) { instanceMethodsHash ^= item.GetHashCode(); }
            hash.Add(instanceMethodsHash);

            hash.Add(RefMask);

            int paramsHash = 0;
            foreach (string item in Parameters) { paramsHash ^= item.GetHashCode(); }
            hash.Add(paramsHash);

            foreach (string item in EnumMembers) { hash.Add(item); }

            return hash.ToHashCode();
        }

        public override string ToString() => $"<internal: Type_Metadata__{Name}>";
    }
}