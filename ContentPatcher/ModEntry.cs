using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using ContentPatcher.Framework;
using ContentPatcher.Framework.Conditions;
using ContentPatcher.Framework.ConfigModels;
using ContentPatcher.Framework.Patches;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace ContentPatcher
{
    /// <summary>The mod entry point.</summary>
    internal class ModEntry : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The name of the file which contains patch metadata.</summary>
        private readonly string PatchFileName = "content.json";

        /// <summary>The name of the file which contains player settings.</summary>
        private readonly string ConfigFileName = "config.json";

        /// <summary>Handles loading assets from content packs.</summary>
        private readonly AssetLoader AssetLoader = new AssetLoader();

        /// <summary>Manages loaded patches.</summary>
        private PatchManager PatchManager;

        /// <summary>The mod configuration.</summary>
        private ModConfig Config;

        /// <summary>The debug overlay (if enabled).</summary>
        private DebugOverlay DebugOverlay;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            // init
            this.Config = helper.ReadConfig<ModConfig>();
            this.PatchManager = new PatchManager(this.Monitor, helper.Content.CurrentLocaleConstant);
            this.LoadContentPacks();

            // register patcher
            helper.Content.AssetLoaders.Add(this.PatchManager);
            helper.Content.AssetEditors.Add(this.PatchManager);

            // set up events
            if (this.Config.EnableDebugFeatures)
                InputEvents.ButtonPressed += this.InputEvents_ButtonPressed;
            SaveEvents.AfterReturnToTitle += this.SaveEvents_AfterReturnToTitle;
            TimeEvents.AfterDayStarted += this.TimeEvents_AfterDayStarted;
        }


        /*********
        ** Private methods
        *********/
        /****
        ** Event handlers
        ****/
        /// <summary>The method invoked when the player presses a button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void InputEvents_ButtonPressed(object sender, EventArgsInput e)
        {
            if (this.Config.EnableDebugFeatures)
            {
                // toggle overlay
                if (this.Config.Controls.ToggleDebug.Contains(e.Button))
                {
                    if (this.DebugOverlay == null)
                        this.DebugOverlay = new DebugOverlay(this.Helper.Content);
                    else
                    {
                        this.DebugOverlay.Dispose();
                        this.DebugOverlay = null;
                    }
                    return;
                }

                // cycle textures
                if (this.DebugOverlay != null)
                {
                    if (this.Config.Controls.DebugPrevTexture.Contains(e.Button))
                        this.DebugOverlay.PrevTexture();
                    if (this.Config.Controls.DebugNextTexture.Contains(e.Button))
                        this.DebugOverlay.NextTexture();
                }
            }
        }

        /// <summary>The method invoked when the player returns to the title screen.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void SaveEvents_AfterReturnToTitle(object sender, EventArgs e)
        {
            // get context
            IContentHelper contentHelper = this.Helper.Content;
            LocalizedContentManager.LanguageCode language = contentHelper.CurrentLocaleConstant;

            // update context
            if (this.Config.VerboseLog)
                this.Monitor.Log($"Context: date=none, weather=none, locale={language}.", LogLevel.Trace);
            this.PatchManager.UpdateContext(this.Helper.Content, this.Helper.Content.CurrentLocaleConstant, null, null);
        }

        /// <summary>The method invoked when a new day starts.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void TimeEvents_AfterDayStarted(object sender, EventArgs e)
        {
            // get context
            IContentHelper contentHelper = this.Helper.Content;
            LocalizedContentManager.LanguageCode language = contentHelper.CurrentLocaleConstant;
            SDate date = SDate.Now();
            Weather weather = this.GetCurrentWeather();

            // update context
            if (this.Config.VerboseLog)
                this.Monitor.Log($"Context: date={date.DayOfWeek} {date.Season} {date.Day}, weather={weather}, locale={language}.", LogLevel.Trace);
            this.PatchManager.UpdateContext(contentHelper, language, date, weather);
        }

        /****
        ** Methods
        ****/
        /// <summary>Load the patches from all registered content packs.</summary>
        [SuppressMessage("ReSharper", "AccessToModifiedClosure", Justification = "The value is used immediately, so this isn't an issue.")]
        private void LoadContentPacks()
        {
            foreach (IContentPack pack in this.Helper.GetContentPacks())
            {
                if (this.Config.VerboseLog)
                    this.Monitor.Log($"Loading content pack '{pack.Manifest.Name}'...", LogLevel.Trace);

                try
                {
                    // read changes file
                    ContentConfig content = pack.ReadJsonFile<ContentConfig>(this.PatchFileName);
                    if (content == null)
                    {
                        this.Monitor.Log($"Ignored content pack '{pack.Manifest.Name}' because it has no {this.PatchFileName} file.", LogLevel.Warn);
                        continue;
                    }
                    if (content.Format == null || content.Changes == null)
                    {
                        this.Monitor.Log($"Ignored content pack '{pack.Manifest.Name}' because it doesn't specify the required {nameof(ContentConfig.Format)} or {nameof(ContentConfig.Changes)} fields.", LogLevel.Warn);
                        continue;
                    }
                    if (content.Format.ToString() != "1.0")
                    {
                        this.Monitor.Log($"Ignored content pack '{pack.Manifest.Name}' because it uses unsupported format {content.Format} (supported version: 1.0).", LogLevel.Warn);
                        continue;
                    }

                    // load config.json
                    IDictionary<string, HashSet<string>> config;
                    {
                        string configFilePath = Path.Combine(pack.DirectoryPath, this.ConfigFileName);

                        // read schema
                        IDictionary<string, ConfigSchemaField> configSchema = this.LoadConfigSchema(content.ConfigSchema, logWarning: (field, reasonPhrase) => this.Monitor.Log($"Ignored {pack.Manifest.Name} > {nameof(ContentConfig.ConfigSchema)} field '{field}': {reasonPhrase}", LogLevel.Warn));
                        if (!configSchema.Any() && File.Exists(configFilePath))
                            this.Monitor.Log($"Ignored {pack.Manifest.Name} > config.json: this content pack doesn't support configuration.", LogLevel.Warn);

                        // read config
                        config = this.LoadConfig(pack, configSchema, (field, reasonPhrase) => this.Monitor.Log($"Ignored {pack.Manifest.Name} > {this.ConfigFileName} field '{field}': {reasonPhrase}", LogLevel.Warn));

                        // save normalised config
                        if (config.Any())
                            this.Helper.WriteJsonFile(configFilePath, config.ToDictionary(p => p.Key, p => string.Join(", ", p.Value)));
                    }

                    // load patches
                    int i = 0;
                    foreach (PatchConfig entry in content.Changes)
                    {
                        i++;
                        this.LoadPatch(pack, entry, config, logSkip: reasonPhrase => this.Monitor.Log($"Ignored {pack.Manifest.Name} > entry #{i}: {reasonPhrase}", LogLevel.Warn));
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Error loading content pack '{pack.Manifest.Name}'. Technical details:\n{ex}", LogLevel.Error);
                }
            }
        }

        /// <summary>Parse a raw config schema for a content pack.</summary>
        /// <param name="rawSchema">The raw config schema.</param>
        /// <param name="logWarning">The callback to invoke on each validation warning, passed the field name and reason respectively.</param>
        private IDictionary<string, ConfigSchemaField> LoadConfigSchema(IDictionary<string, ConfigSchemaFieldConfig> rawSchema, Action<string, string> logWarning)
        {
            IDictionary<string, ConfigSchemaField> schema = new Dictionary<string, ConfigSchemaField>(StringComparer.InvariantCultureIgnoreCase);
            if (rawSchema == null || !rawSchema.Any())
                return schema;

            foreach (string key in rawSchema.Keys)
            {
                ConfigSchemaFieldConfig field = rawSchema[key];

                // validate key
                if (Enum.TryParse(key, true, out ConditionKey conditionKey))
                {
                    logWarning(key, $"can't use {conditionKey} as a config field, because it's a reserved condition name.");
                    continue;
                }

                // read allowed values
                HashSet<string> allowValues = this.ParseCommaDelimitedField(field.AllowValues);
                if (!allowValues.Any())
                {
                    logWarning(key, $"no {nameof(ConfigSchemaFieldConfig.AllowValues)} specified.");
                    continue;
                }

                // read default values
                HashSet<string> defaultValues = this.ParseCommaDelimitedField(field.Default);
                {
                    // inject default
                    if (!defaultValues.Any() && !field.AllowBlank)
                        defaultValues = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { allowValues.First() };

                    // validate values
                    string[] invalidValues = defaultValues.Except(allowValues).ToArray();
                    if (invalidValues.Any())
                    {
                        logWarning(key, $"default values '{string.Join(", ", invalidValues)}' are not allowed according to {nameof(ConfigSchemaFieldConfig.AllowBlank)}.");
                        continue;
                    }

                    // validate allow multiple
                    if (!field.AllowMultiple && defaultValues.Count > 1)
                    {
                        logWarning(key, $"can't have multiple default values because {nameof(ConfigSchemaFieldConfig.AllowMultiple)} is false.");
                        continue;
                    }
                }

                // add to schema
                schema[key] = new ConfigSchemaField(allowValues, defaultValues, field.AllowBlank, field.AllowMultiple);
            }

            return schema;
        }

        /// <summary>Load a config file from the content pack.</summary>
        /// <param name="contentPack">The content pack whose config file to read.</param>
        /// <param name="schema">The config schema.</param>
        /// <param name="logWarning">The callback to invoke on each validation warning, passed the field name and reason respectively.</param>
        private IDictionary<string, HashSet<string>> LoadConfig(IContentPack contentPack, IDictionary<string, ConfigSchemaField> schema, Action<string, string> logWarning)
        {
            if (schema == null || !schema.Any())
                return new Dictionary<string, HashSet<string>>(0, StringComparer.InvariantCultureIgnoreCase);

            // read raw config
            IDictionary<string, HashSet<string>> config =
                (contentPack.ReadJsonFile<Dictionary<string, string>>(this.ConfigFileName) ?? new Dictionary<string, string>())
                .ToDictionary(entry => entry.Key.Trim(), entry => this.ParseCommaDelimitedField(entry.Value), StringComparer.InvariantCultureIgnoreCase);

            // remove invalid values
            foreach (string key in config.Keys)
            {
                if (!schema.ContainsKey(key))
                {
                    logWarning(key, "no such field supported by this content pack.");
                    config.Remove(key);
                }
            }

            // inject default values
            foreach (string key in schema.Keys)
            {
                ConfigSchemaField fieldSchema = schema[key];
                if (!config.TryGetValue(key, out HashSet<string> values) || (!fieldSchema.AllowBlank && !values.Any()))
                    config[key] = fieldSchema.DefaultValues;
            }

            // parse each field
            foreach (string key in schema.Keys)
            {
                ConfigSchemaField schemaField = schema[key];
                HashSet<string> actualValues = config[key];

                // validate allow-multiple
                if (!schemaField.AllowMultiple && actualValues.Count > 1)
                {
                    logWarning(key, "field only allows a single value.");
                    config[key] = schemaField.DefaultValues;
                    continue;
                }

                // validate allow-values
                string[] invalidValues = actualValues.Except(schemaField.AllowValues).ToArray();
                if (invalidValues.Any())
                {
                    logWarning(key, $"found invalid values ({string.Join(", ", invalidValues)}), expected: {string.Join(", ", schemaField.AllowValues)}.");
                    config[key] = schemaField.DefaultValues;
                    continue;
                }
            }

            return config;
        }

        /// <summary>Load one patch from a content pack's <c>content.json</c> file.</summary>
        /// <param name="pack">The content pack being loaded.</param>
        /// <param name="entry">The change to load.</param>
        /// <param name="config">The config values to apply.</param>
        /// <param name="logSkip">The callback to invoke with the error reason if loading it fails.</param>
        private void LoadPatch(IContentPack pack, PatchConfig entry, IDictionary<string, HashSet<string>> config, Action<string> logSkip)
        {
            try
            {
                // skip if disabled
                if (!entry.Enabled)
                    return;

                // parse action
                if (!Enum.TryParse(entry.Action, out PatchType action))
                {
                    logSkip(string.IsNullOrWhiteSpace(entry.Action)
                        ? $"must set the {nameof(PatchConfig.Action)} field."
                        : $"invalid {nameof(PatchConfig.Action)} value '{entry.Action}', expected one of: {string.Join(", ", Enum.GetNames(typeof(PatchType)))}."
                    );
                    return;
                }

                // parse target asset
                string assetName = !string.IsNullOrWhiteSpace(entry.Target)
                    ? this.Helper.Content.NormaliseAssetName(entry.Target)
                    : null;
                if (assetName == null)
                {
                    logSkip($"must set the {nameof(PatchConfig.Target)} field.");
                    return;
                }

                // parse source asset
                string localAsset = this.NormaliseLocalAssetPath(pack, entry.FromFile);
                if (localAsset == null && (action == PatchType.Load || action == PatchType.EditImage))
                {
                    logSkip($"must set the {nameof(PatchConfig.FromFile)} field for action '{action}'.");
                    return;
                }
                if (localAsset != null)
                {
                    localAsset = this.AssetLoader.GetActualPath(pack, localAsset);
                    if (localAsset == null)
                    {
                        logSkip($"the {nameof(PatchConfig.FromFile)} field specifies a file that doesn't exist: {entry.FromFile}.");
                        return;
                    }
                }

                // apply config
                foreach (string key in config.Keys)
                {
                    if (entry.When.TryGetValue(key, out string values))
                    {
                        HashSet<string> expected = this.ParseCommaDelimitedField(values);
                        if (!expected.Intersect(config[key]).Any())
                            return;

                        entry.When.Remove(key);
                    }
                }

                // parse conditions
                ConditionDictionary conditions;
                {
                    if (!this.PatchManager.TryParseConditions(entry.When, out conditions, out string error))
                    {
                        logSkip($"the {nameof(PatchConfig.When)} field is invalid: {error}.");
                        return;
                    }
                }

                // parse & save patch
                switch (action)
                {
                    // load asset
                    case PatchType.Load:
                        {
                            // init patch
                            IPatch patch = new LoadPatch(this.AssetLoader, pack, assetName, conditions, localAsset);

                            // detect conflicting loaders
                            IPatch[] conflictingLoaders = this.PatchManager.GetConflictingLoaders(patch).ToArray();
                            if (conflictingLoaders.Any())
                            {
                                if (conflictingLoaders.Any(p => p.ContentPack == pack))
                                    logSkip($"the {assetName} file is already being loaded by this content pack. Each file can only be loaded once (unless their conditions can't overlap).");
                                else
                                {
                                    string[] conflictingNames = conflictingLoaders.Select(p => p.ContentPack.Manifest.Name).Distinct().OrderBy(p => p).ToArray();
                                    logSkip($"the {assetName} file is already being loaded by {(conflictingNames.Length == 1 ? "another content pack" : "other content packs")} ({string.Join(", ", conflictingNames)}). Each file can only be loaded once (unless their conditions can't overlap).");
                                }
                                return;
                            }

                            // add
                            this.PatchManager.Add(patch);
                        }
                        break;

                    // edit data
                    case PatchType.EditData:
                        {
                            // validate
                            if (entry.Entries == null && entry.Fields == null)
                            {
                                logSkip($"either {nameof(PatchConfig.Entries)} or {nameof(PatchConfig.Fields)} must be specified for a '{action}' change.");
                                return;
                            }
                            if (entry.Entries != null && entry.Entries.Any(p => string.IsNullOrWhiteSpace(p.Value)))
                            {
                                logSkip($"the {nameof(PatchConfig.Entries)} can't contain empty values.");
                                return;
                            }
                            if (entry.Fields != null && entry.Fields.Any(p => p.Value == null || p.Value.Any(n => n.Value == null)))
                            {
                                logSkip($"the {nameof(PatchConfig.Fields)} can't contain empty values.");
                                return;
                            }

                            // save
                            this.PatchManager.Add(new EditDataPatch(this.AssetLoader, pack, assetName, conditions, entry.Entries, entry.Fields, this.Monitor));
                        }
                        break;

                    // edit image
                    case PatchType.EditImage:
                        // read patch mode
                        PatchMode patchMode = PatchMode.Replace;
                        if (!string.IsNullOrWhiteSpace(entry.PatchMode) && !Enum.TryParse(entry.PatchMode, true, out patchMode))
                        {
                            logSkip($"the {nameof(PatchConfig.PatchMode)} is invalid. Expected one of these values: [{string.Join(", ", Enum.GetNames(typeof(PatchMode)))}].");
                            return;
                        }

                        // save
                        this.PatchManager.Add(new EditImagePatch(this.AssetLoader, pack, assetName, conditions, localAsset, entry.FromArea, entry.ToArea, patchMode, this.Monitor));
                        break;

                    default:
                        logSkip($"unsupported patch type '{action}'.");
                        break;
                }

                // preload PNG assets to avoid load-in-draw-loop error
                if (localAsset != null)
                    this.AssetLoader.PreloadIfNeeded(pack, localAsset);
            }
            catch (Exception ex)
            {
                logSkip($"error reading info. Technical details:\n{ex}");
            }
        }

        /// <summary>Get a normalised file path relative to the content pack folder.</summary>
        /// <param name="contentPack">The content pack.</param>
        /// <param name="path">The relative asset path.</param>
        private string NormaliseLocalAssetPath(IContentPack contentPack, string path)
        {
            // normalise asset name
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string newPath = this.Helper.Content.NormaliseAssetName(path);

            // add .xnb extension if needed (it's stripped from asset names)
            string fullPath = Path.Combine(contentPack.DirectoryPath, newPath);
            if (!File.Exists(fullPath))
            {
                if (File.Exists($"{fullPath}.xnb") || Path.GetExtension(path) == ".xnb")
                    newPath += ".xnb";
            }

            return newPath;
        }

        /// <summary>Get the current weather from the game state.</summary>
        private Weather GetCurrentWeather()
        {
            if (Utility.isFestivalDay(Game1.dayOfMonth, Game1.currentSeason) || Game1.weddingToday)
                return Weather.Sun;

            if (Game1.isSnowing)
                return Weather.Snow;
            if (Game1.isRaining)
                return Game1.isLightning ? Weather.Storm : Weather.Rain;

            return Weather.Sun;
        }

        /// <summary>Parse a comma-delimited set of case-insensitive condition values.</summary>
        /// <param name="field">The field value to parse.</param>
        private HashSet<string> ParseCommaDelimitedField(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
                return new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            IEnumerable<string> values = (
                from value in field.Split(',')
                where !string.IsNullOrWhiteSpace(value)
                select value.Trim().ToLower()
            );
            return new HashSet<string>(values, StringComparer.InvariantCultureIgnoreCase);
        }
    }
}
