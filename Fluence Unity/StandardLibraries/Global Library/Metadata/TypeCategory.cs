namespace Fluence.Unity.Global
{
    /// <summary>
    /// Describes the category of a Fluence type.
    /// </summary>
    internal enum TypeCategory
    {
        Primitive,  // int, bool, nil, string, etc.
        Struct,
        Enum,
        Function,   // Also lambdas.
        BuiltIn,    // List, Map, Set, etc.
        Internal,   // ReferenceObject, etc.
        Unknown
    }
}