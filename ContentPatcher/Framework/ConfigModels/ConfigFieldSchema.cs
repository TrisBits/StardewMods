using System.Collections.Generic;

namespace ContentPatcher.Framework.ConfigModels
{
    /// <summary>The parsed schema for a field in the <c>config.json</c> file.</summary>
    internal class ConfigSchemaField
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The values to allow.</summary>
        public HashSet<string> AllowValues { get; }

        /// <summary>The default values if the field is missing or (if <see cref="AllowBlank"/> is <c>false</c>) blank.</summary>
        public HashSet<string> DefaultValues { get; set; }

        /// <summary>Whether to allow blank values.</summary>
        public bool AllowBlank { get; set; }

        /// <summary>Whether the player can specify multiple values for this field.</summary>
        public bool AllowMultiple { get; set; }


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="allowValues">The values to allow.</param>
        /// <param name="defaultValues">The default values if the field is missing or (if <paramref name="allowBlank"/> is <c>false</c>) blank.</param>
        /// <param name="allowBlank">Whether to allow blank values.</param>
        /// <param name="allowMultiple">Whether the player can specify multiple values for this field.</param>
        public ConfigSchemaField(HashSet<string> allowValues, HashSet<string> defaultValues, bool allowBlank, bool allowMultiple)
        {
            this.AllowValues = allowValues;
            this.DefaultValues = defaultValues;
            this.AllowBlank = allowBlank;
            this.AllowMultiple = allowMultiple;
        }
    }
}
