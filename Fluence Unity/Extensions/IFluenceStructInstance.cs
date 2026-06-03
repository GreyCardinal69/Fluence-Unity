namespace Fluence.Unity
{
    /// <summary>
    /// A public interface representing a Fluence struct instance.
    /// Allows the host application to get and set fields on a script object.
    /// </summary>
    public interface IFluenceStructInstance
    {
        /// <summary>
        /// Gets the name of the struct blueprint (class) this instance belongs to.
        /// </summary>
        string StructName { get; }

        /// <summary>
        /// Retrieves the value of a field, converted to a standard C# type.
        /// </summary>
        /// <param name="fieldName">The name of the field.</param>
        /// <returns>The C# value.</returns>
        object? GetField(string fieldName);

        /// <summary>
        /// Sets the value of a field using a standard C# type.
        /// </summary>
        /// <param name="fieldName">The name of the field to set.</param>
        /// <param name="value">The new value.</param>
        void SetField(string fieldName, object? value);

        /// <summary>
        /// Returns a list of all active instance field names.
        /// </summary>
        IEnumerable<string> GetFieldNames();
    }
}