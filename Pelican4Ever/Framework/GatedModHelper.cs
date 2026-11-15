using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Envoltorio de <see cref="IModHelper"/> que filtra las suscripciones a eventos de SMAPI mediante un predicado de activación.
    /// Permite desactivar los manejadores de eventos de todos los módulos sin modificar cada archivo individualmente.
    /// </summary>
    public sealed class GatedModHelper : IModHelper
    {
        private readonly IModHelper inner;

        public GatedModHelper(IModHelper inner, Func<bool> isEnabled)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (isEnabled == null)
                throw new ArgumentNullException(nameof(isEnabled));

            Events = new GatedModEvents(inner.Events, isEnabled);
        }

        public string DirectoryPath => inner.DirectoryPath;

        public IModEvents Events { get; }

        public ICommandHelper ConsoleCommands => inner.ConsoleCommands;

        public IGameContentHelper GameContent => inner.GameContent;

        public IModContentHelper ModContent => inner.ModContent;

        public IContentPackHelper ContentPacks => inner.ContentPacks;

        public IDataHelper Data => inner.Data;

        public IInputHelper Input => inner.Input;

        public IReflectionHelper Reflection => inner.Reflection;

        public IModRegistry ModRegistry => inner.ModRegistry;

        public IMultiplayerHelper Multiplayer => inner.Multiplayer;

        public ITranslationHelper Translation => inner.Translation;

        public TConfig ReadConfig<TConfig>() where TConfig : class, new()
            => inner.ReadConfig<TConfig>();

        public void WriteConfig<TConfig>(TConfig config) where TConfig : class, new()
            => inner.WriteConfig(config);
    }

    /// <summary>
    /// Contenedor de eventos de SMAPI con filtrado dinámico para los manejadores registrados por módulos de Pelican 4Ever.
    /// </summary>
    internal sealed class GatedModEvents : IModEvents
    {
        public GatedModEvents(IModEvents inner, Func<bool> isEnabled)
        {
            var router = new GatedEventRouter(isEnabled);
            Content = new GatedContentEvents(inner.Content, router);
            Display = new GatedDisplayEvents(inner.Display, router);
            GameLoop = new GatedGameLoopEvents(inner.GameLoop, router);
            Input = new GatedInputEvents(inner.Input, router);
            Multiplayer = new GatedMultiplayerEvents(inner.Multiplayer, router);
            Player = new GatedPlayerEvents(inner.Player, router);
            World = new GatedWorldEvents(inner.World, router);
            Specialized = new GatedSpecializedEvents(inner.Specialized, router);
        }

        public IContentEvents Content { get; }

        public IDisplayEvents Display { get; }

        public IGameLoopEvents GameLoop { get; }

        public IInputEvents Input { get; }

        public IMultiplayerEvents Multiplayer { get; }

        public IPlayerEvents Player { get; }

        public IWorldEvents World { get; }

        public ISpecializedEvents Specialized { get; }
    }

    /// <summary>
    /// Enruta suscripciones y desuscripciones de eventos envolviendo los delegados con la comprobación <see cref="isEnabled"/>.
    /// </summary>
    internal sealed class GatedEventRouter
    {
        private readonly Func<bool> isEnabled;
        private readonly Dictionary<(string EventName, Delegate Original), List<Delegate>> mappings = new();

        public GatedEventRouter(Func<bool> isEnabled)
        {
            this.isEnabled = isEnabled;
        }

        public void Subscribe<TEventArgs>(
            string eventName,
            Action<EventHandler<TEventArgs>> addHandler,
            EventHandler<TEventArgs>? handler)
        {
            if (handler == null)
                return;

            EventHandler<TEventArgs> wrapped = (sender, args) =>
            {
                if (isEnabled())
                    handler(sender, args);
            };

            lock (mappings)
            {
                var key = (eventName, (Delegate)handler);
                if (!mappings.TryGetValue(key, out List<Delegate>? list))
                {
                    list = new List<Delegate>();
                    mappings[key] = list;
                }

                list.Add(wrapped);
            }

            addHandler(wrapped);
        }

        public void Unsubscribe<TEventArgs>(
            string eventName,
            Action<EventHandler<TEventArgs>> removeHandler,
            EventHandler<TEventArgs>? handler)
        {
            if (handler == null)
                return;

            EventHandler<TEventArgs>? wrapped = null;
            lock (mappings)
            {
                var key = (eventName, (Delegate)handler);
                if (mappings.TryGetValue(key, out List<Delegate>? list) && list.Count > 0)
                {
                    int lastIndex = list.Count - 1;
                    wrapped = (EventHandler<TEventArgs>)list[lastIndex];
                    list.RemoveAt(lastIndex);
                    if (list.Count == 0)
                        mappings.Remove(key);
                }
            }

            removeHandler(wrapped ?? handler);
        }
    }

    internal sealed class GatedContentEvents : IContentEvents
    {
        private readonly IContentEvents inner;
        private readonly GatedEventRouter router;

        public GatedContentEvents(IContentEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<AssetRequestedEventArgs> AssetRequested
        {
            add => router.Subscribe<AssetRequestedEventArgs>(nameof(AssetRequested), h => inner.AssetRequested += h, value);
            remove => router.Unsubscribe<AssetRequestedEventArgs>(nameof(AssetRequested), h => inner.AssetRequested -= h, value);
        }

        public event EventHandler<AssetsInvalidatedEventArgs> AssetsInvalidated
        {
            add => router.Subscribe<AssetsInvalidatedEventArgs>(nameof(AssetsInvalidated), h => inner.AssetsInvalidated += h, value);
            remove => router.Unsubscribe<AssetsInvalidatedEventArgs>(nameof(AssetsInvalidated), h => inner.AssetsInvalidated -= h, value);
        }

        public event EventHandler<AssetReadyEventArgs> AssetReady
        {
            add => router.Subscribe<AssetReadyEventArgs>(nameof(AssetReady), h => inner.AssetReady += h, value);
            remove => router.Unsubscribe<AssetReadyEventArgs>(nameof(AssetReady), h => inner.AssetReady -= h, value);
        }

        public event EventHandler<LocaleChangedEventArgs> LocaleChanged
        {
            add => router.Subscribe<LocaleChangedEventArgs>(nameof(LocaleChanged), h => inner.LocaleChanged += h, value);
            remove => router.Unsubscribe<LocaleChangedEventArgs>(nameof(LocaleChanged), h => inner.LocaleChanged -= h, value);
        }
    }

    internal sealed class GatedDisplayEvents : IDisplayEvents
    {
        private readonly IDisplayEvents inner;
        private readonly GatedEventRouter router;

        public GatedDisplayEvents(IDisplayEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<MenuChangedEventArgs> MenuChanged
        {
            add => router.Subscribe<MenuChangedEventArgs>(nameof(MenuChanged), h => inner.MenuChanged += h, value);
            remove => router.Unsubscribe<MenuChangedEventArgs>(nameof(MenuChanged), h => inner.MenuChanged -= h, value);
        }

        public event EventHandler<RenderingStepEventArgs> RenderingStep
        {
            add => router.Subscribe<RenderingStepEventArgs>(nameof(RenderingStep), h => inner.RenderingStep += h, value);
            remove => router.Unsubscribe<RenderingStepEventArgs>(nameof(RenderingStep), h => inner.RenderingStep -= h, value);
        }

        public event EventHandler<RenderedStepEventArgs> RenderedStep
        {
            add => router.Subscribe<RenderedStepEventArgs>(nameof(RenderedStep), h => inner.RenderedStep += h, value);
            remove => router.Unsubscribe<RenderedStepEventArgs>(nameof(RenderedStep), h => inner.RenderedStep -= h, value);
        }

        public event EventHandler<RenderingEventArgs> Rendering
        {
            add => router.Subscribe<RenderingEventArgs>(nameof(Rendering), h => inner.Rendering += h, value);
            remove => router.Unsubscribe<RenderingEventArgs>(nameof(Rendering), h => inner.Rendering -= h, value);
        }

        public event EventHandler<RenderedEventArgs> Rendered
        {
            add => router.Subscribe<RenderedEventArgs>(nameof(Rendered), h => inner.Rendered += h, value);
            remove => router.Unsubscribe<RenderedEventArgs>(nameof(Rendered), h => inner.Rendered -= h, value);
        }

        public event EventHandler<RenderingWorldEventArgs> RenderingWorld
        {
            add => router.Subscribe<RenderingWorldEventArgs>(nameof(RenderingWorld), h => inner.RenderingWorld += h, value);
            remove => router.Unsubscribe<RenderingWorldEventArgs>(nameof(RenderingWorld), h => inner.RenderingWorld -= h, value);
        }

        public event EventHandler<RenderedWorldEventArgs> RenderedWorld
        {
            add => router.Subscribe<RenderedWorldEventArgs>(nameof(RenderedWorld), h => inner.RenderedWorld += h, value);
            remove => router.Unsubscribe<RenderedWorldEventArgs>(nameof(RenderedWorld), h => inner.RenderedWorld -= h, value);
        }

        public event EventHandler<RenderingActiveMenuEventArgs> RenderingActiveMenu
        {
            add => router.Subscribe<RenderingActiveMenuEventArgs>(nameof(RenderingActiveMenu), h => inner.RenderingActiveMenu += h, value);
            remove => router.Unsubscribe<RenderingActiveMenuEventArgs>(nameof(RenderingActiveMenu), h => inner.RenderingActiveMenu -= h, value);
        }

        public event EventHandler<RenderedActiveMenuEventArgs> RenderedActiveMenu
        {
            add => router.Subscribe<RenderedActiveMenuEventArgs>(nameof(RenderedActiveMenu), h => inner.RenderedActiveMenu += h, value);
            remove => router.Unsubscribe<RenderedActiveMenuEventArgs>(nameof(RenderedActiveMenu), h => inner.RenderedActiveMenu -= h, value);
        }

        public event EventHandler<RenderingHudEventArgs> RenderingHud
        {
            add => router.Subscribe<RenderingHudEventArgs>(nameof(RenderingHud), h => inner.RenderingHud += h, value);
            remove => router.Unsubscribe<RenderingHudEventArgs>(nameof(RenderingHud), h => inner.RenderingHud -= h, value);
        }

        public event EventHandler<RenderedHudEventArgs> RenderedHud
        {
            add => router.Subscribe<RenderedHudEventArgs>(nameof(RenderedHud), h => inner.RenderedHud += h, value);
            remove => router.Unsubscribe<RenderedHudEventArgs>(nameof(RenderedHud), h => inner.RenderedHud -= h, value);
        }

        public event EventHandler<WindowResizedEventArgs> WindowResized
        {
            add => router.Subscribe<WindowResizedEventArgs>(nameof(WindowResized), h => inner.WindowResized += h, value);
            remove => router.Unsubscribe<WindowResizedEventArgs>(nameof(WindowResized), h => inner.WindowResized -= h, value);
        }
    }

    internal sealed class GatedGameLoopEvents : IGameLoopEvents
    {
        private readonly IGameLoopEvents inner;
        private readonly GatedEventRouter router;

        public GatedGameLoopEvents(IGameLoopEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        // GameLaunched realiza inicializaciones únicas de comandos y parches al arranque,
        // por lo que pasa sin filtrar y posteriormente HarmonyPatchToggle gestiona los parches.
        public event EventHandler<GameLaunchedEventArgs> GameLaunched
        {
            add => inner.GameLaunched += value;
            remove => inner.GameLaunched -= value;
        }

        public event EventHandler<UpdateTickingEventArgs> UpdateTicking
        {
            add => router.Subscribe<UpdateTickingEventArgs>(nameof(UpdateTicking), h => inner.UpdateTicking += h, value);
            remove => router.Unsubscribe<UpdateTickingEventArgs>(nameof(UpdateTicking), h => inner.UpdateTicking -= h, value);
        }

        public event EventHandler<UpdateTickedEventArgs> UpdateTicked
        {
            add => router.Subscribe<UpdateTickedEventArgs>(nameof(UpdateTicked), h => inner.UpdateTicked += h, value);
            remove => router.Unsubscribe<UpdateTickedEventArgs>(nameof(UpdateTicked), h => inner.UpdateTicked -= h, value);
        }

        public event EventHandler<OneSecondUpdateTickingEventArgs> OneSecondUpdateTicking
        {
            add => router.Subscribe<OneSecondUpdateTickingEventArgs>(nameof(OneSecondUpdateTicking), h => inner.OneSecondUpdateTicking += h, value);
            remove => router.Unsubscribe<OneSecondUpdateTickingEventArgs>(nameof(OneSecondUpdateTicking), h => inner.OneSecondUpdateTicking -= h, value);
        }

        public event EventHandler<OneSecondUpdateTickedEventArgs> OneSecondUpdateTicked
        {
            add => router.Subscribe<OneSecondUpdateTickedEventArgs>(nameof(OneSecondUpdateTicked), h => inner.OneSecondUpdateTicked += h, value);
            remove => router.Unsubscribe<OneSecondUpdateTickedEventArgs>(nameof(OneSecondUpdateTicked), h => inner.OneSecondUpdateTicked -= h, value);
        }

        public event EventHandler<SaveCreatingEventArgs> SaveCreating
        {
            add => router.Subscribe<SaveCreatingEventArgs>(nameof(SaveCreating), h => inner.SaveCreating += h, value);
            remove => router.Unsubscribe<SaveCreatingEventArgs>(nameof(SaveCreating), h => inner.SaveCreating -= h, value);
        }

        public event EventHandler<SaveCreatedEventArgs> SaveCreated
        {
            add => router.Subscribe<SaveCreatedEventArgs>(nameof(SaveCreated), h => inner.SaveCreated += h, value);
            remove => router.Unsubscribe<SaveCreatedEventArgs>(nameof(SaveCreated), h => inner.SaveCreated -= h, value);
        }

        public event EventHandler<SavingEventArgs> Saving
        {
            add => router.Subscribe<SavingEventArgs>(nameof(Saving), h => inner.Saving += h, value);
            remove => router.Unsubscribe<SavingEventArgs>(nameof(Saving), h => inner.Saving -= h, value);
        }

        public event EventHandler<SavedEventArgs> Saved
        {
            add => router.Subscribe<SavedEventArgs>(nameof(Saved), h => inner.Saved += h, value);
            remove => router.Unsubscribe<SavedEventArgs>(nameof(Saved), h => inner.Saved -= h, value);
        }

        public event EventHandler<SaveLoadedEventArgs> SaveLoaded
        {
            add => router.Subscribe<SaveLoadedEventArgs>(nameof(SaveLoaded), h => inner.SaveLoaded += h, value);
            remove => router.Unsubscribe<SaveLoadedEventArgs>(nameof(SaveLoaded), h => inner.SaveLoaded -= h, value);
        }

        public event EventHandler<DayStartedEventArgs> DayStarted
        {
            add => router.Subscribe<DayStartedEventArgs>(nameof(DayStarted), h => inner.DayStarted += h, value);
            remove => router.Unsubscribe<DayStartedEventArgs>(nameof(DayStarted), h => inner.DayStarted -= h, value);
        }

        public event EventHandler<DayEndingEventArgs> DayEnding
        {
            add => router.Subscribe<DayEndingEventArgs>(nameof(DayEnding), h => inner.DayEnding += h, value);
            remove => router.Unsubscribe<DayEndingEventArgs>(nameof(DayEnding), h => inner.DayEnding -= h, value);
        }

        public event EventHandler<TimeChangedEventArgs> TimeChanged
        {
            add => router.Subscribe<TimeChangedEventArgs>(nameof(TimeChanged), h => inner.TimeChanged += h, value);
            remove => router.Unsubscribe<TimeChangedEventArgs>(nameof(TimeChanged), h => inner.TimeChanged -= h, value);
        }

        public event EventHandler<ReturnedToTitleEventArgs> ReturnedToTitle
        {
            add => router.Subscribe<ReturnedToTitleEventArgs>(nameof(ReturnedToTitle), h => inner.ReturnedToTitle += h, value);
            remove => router.Unsubscribe<ReturnedToTitleEventArgs>(nameof(ReturnedToTitle), h => inner.ReturnedToTitle -= h, value);
        }
    }

    internal sealed class GatedInputEvents : IInputEvents
    {
        private readonly IInputEvents inner;
        private readonly GatedEventRouter router;

        public GatedInputEvents(IInputEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<ButtonsChangedEventArgs> ButtonsChanged
        {
            add => router.Subscribe<ButtonsChangedEventArgs>(nameof(ButtonsChanged), h => inner.ButtonsChanged += h, value);
            remove => router.Unsubscribe<ButtonsChangedEventArgs>(nameof(ButtonsChanged), h => inner.ButtonsChanged -= h, value);
        }

        public event EventHandler<ButtonPressedEventArgs> ButtonPressed
        {
            add => router.Subscribe<ButtonPressedEventArgs>(nameof(ButtonPressed), h => inner.ButtonPressed += h, value);
            remove => router.Unsubscribe<ButtonPressedEventArgs>(nameof(ButtonPressed), h => inner.ButtonPressed -= h, value);
        }

        public event EventHandler<ButtonReleasedEventArgs> ButtonReleased
        {
            add => router.Subscribe<ButtonReleasedEventArgs>(nameof(ButtonReleased), h => inner.ButtonReleased += h, value);
            remove => router.Unsubscribe<ButtonReleasedEventArgs>(nameof(ButtonReleased), h => inner.ButtonReleased -= h, value);
        }

        public event EventHandler<CursorMovedEventArgs> CursorMoved
        {
            add => router.Subscribe<CursorMovedEventArgs>(nameof(CursorMoved), h => inner.CursorMoved += h, value);
            remove => router.Unsubscribe<CursorMovedEventArgs>(nameof(CursorMoved), h => inner.CursorMoved -= h, value);
        }

        public event EventHandler<MouseWheelScrolledEventArgs> MouseWheelScrolled
        {
            add => router.Subscribe<MouseWheelScrolledEventArgs>(nameof(MouseWheelScrolled), h => inner.MouseWheelScrolled += h, value);
            remove => router.Unsubscribe<MouseWheelScrolledEventArgs>(nameof(MouseWheelScrolled), h => inner.MouseWheelScrolled -= h, value);
        }
    }

    internal sealed class GatedMultiplayerEvents : IMultiplayerEvents
    {
        private readonly IMultiplayerEvents inner;
        private readonly GatedEventRouter router;

        public GatedMultiplayerEvents(IMultiplayerEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<PeerContextReceivedEventArgs> PeerContextReceived
        {
            add => router.Subscribe<PeerContextReceivedEventArgs>(nameof(PeerContextReceived), h => inner.PeerContextReceived += h, value);
            remove => router.Unsubscribe<PeerContextReceivedEventArgs>(nameof(PeerContextReceived), h => inner.PeerContextReceived -= h, value);
        }

        public event EventHandler<PeerConnectedEventArgs> PeerConnected
        {
            add => router.Subscribe<PeerConnectedEventArgs>(nameof(PeerConnected), h => inner.PeerConnected += h, value);
            remove => router.Unsubscribe<PeerConnectedEventArgs>(nameof(PeerConnected), h => inner.PeerConnected -= h, value);
        }

        public event EventHandler<ModMessageReceivedEventArgs> ModMessageReceived
        {
            add => router.Subscribe<ModMessageReceivedEventArgs>(nameof(ModMessageReceived), h => inner.ModMessageReceived += h, value);
            remove => router.Unsubscribe<ModMessageReceivedEventArgs>(nameof(ModMessageReceived), h => inner.ModMessageReceived -= h, value);
        }

        public event EventHandler<PeerDisconnectedEventArgs> PeerDisconnected
        {
            add => router.Subscribe<PeerDisconnectedEventArgs>(nameof(PeerDisconnected), h => inner.PeerDisconnected += h, value);
            remove => router.Unsubscribe<PeerDisconnectedEventArgs>(nameof(PeerDisconnected), h => inner.PeerDisconnected -= h, value);
        }
    }

    internal sealed class GatedPlayerEvents : IPlayerEvents
    {
        private readonly IPlayerEvents inner;
        private readonly GatedEventRouter router;

        public GatedPlayerEvents(IPlayerEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<InventoryChangedEventArgs> InventoryChanged
        {
            add => router.Subscribe<InventoryChangedEventArgs>(nameof(InventoryChanged), h => inner.InventoryChanged += h, value);
            remove => router.Unsubscribe<InventoryChangedEventArgs>(nameof(InventoryChanged), h => inner.InventoryChanged -= h, value);
        }

        public event EventHandler<LevelChangedEventArgs> LevelChanged
        {
            add => router.Subscribe<LevelChangedEventArgs>(nameof(LevelChanged), h => inner.LevelChanged += h, value);
            remove => router.Unsubscribe<LevelChangedEventArgs>(nameof(LevelChanged), h => inner.LevelChanged -= h, value);
        }

        public event EventHandler<WarpedEventArgs> Warped
        {
            add => router.Subscribe<WarpedEventArgs>(nameof(Warped), h => inner.Warped += h, value);
            remove => router.Unsubscribe<WarpedEventArgs>(nameof(Warped), h => inner.Warped -= h, value);
        }
    }

    internal sealed class GatedWorldEvents : IWorldEvents
    {
        private readonly IWorldEvents inner;
        private readonly GatedEventRouter router;

        public GatedWorldEvents(IWorldEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<LocationListChangedEventArgs> LocationListChanged
        {
            add => router.Subscribe<LocationListChangedEventArgs>(nameof(LocationListChanged), h => inner.LocationListChanged += h, value);
            remove => router.Unsubscribe<LocationListChangedEventArgs>(nameof(LocationListChanged), h => inner.LocationListChanged -= h, value);
        }

        public event EventHandler<BuildingListChangedEventArgs> BuildingListChanged
        {
            add => router.Subscribe<BuildingListChangedEventArgs>(nameof(BuildingListChanged), h => inner.BuildingListChanged += h, value);
            remove => router.Unsubscribe<BuildingListChangedEventArgs>(nameof(BuildingListChanged), h => inner.BuildingListChanged -= h, value);
        }

        public event EventHandler<DebrisListChangedEventArgs> DebrisListChanged
        {
            add => router.Subscribe<DebrisListChangedEventArgs>(nameof(DebrisListChanged), h => inner.DebrisListChanged += h, value);
            remove => router.Unsubscribe<DebrisListChangedEventArgs>(nameof(DebrisListChanged), h => inner.DebrisListChanged -= h, value);
        }

        public event EventHandler<LargeTerrainFeatureListChangedEventArgs> LargeTerrainFeatureListChanged
        {
            add => router.Subscribe<LargeTerrainFeatureListChangedEventArgs>(nameof(LargeTerrainFeatureListChanged), h => inner.LargeTerrainFeatureListChanged += h, value);
            remove => router.Unsubscribe<LargeTerrainFeatureListChangedEventArgs>(nameof(LargeTerrainFeatureListChanged), h => inner.LargeTerrainFeatureListChanged -= h, value);
        }

        public event EventHandler<NpcListChangedEventArgs> NpcListChanged
        {
            add => router.Subscribe<NpcListChangedEventArgs>(nameof(NpcListChanged), h => inner.NpcListChanged += h, value);
            remove => router.Unsubscribe<NpcListChangedEventArgs>(nameof(NpcListChanged), h => inner.NpcListChanged -= h, value);
        }

        public event EventHandler<ObjectListChangedEventArgs> ObjectListChanged
        {
            add => router.Subscribe<ObjectListChangedEventArgs>(nameof(ObjectListChanged), h => inner.ObjectListChanged += h, value);
            remove => router.Unsubscribe<ObjectListChangedEventArgs>(nameof(ObjectListChanged), h => inner.ObjectListChanged -= h, value);
        }

        public event EventHandler<ChestInventoryChangedEventArgs> ChestInventoryChanged
        {
            add => router.Subscribe<ChestInventoryChangedEventArgs>(nameof(ChestInventoryChanged), h => inner.ChestInventoryChanged += h, value);
            remove => router.Unsubscribe<ChestInventoryChangedEventArgs>(nameof(ChestInventoryChanged), h => inner.ChestInventoryChanged -= h, value);
        }

        public event EventHandler<TerrainFeatureListChangedEventArgs> TerrainFeatureListChanged
        {
            add => router.Subscribe<TerrainFeatureListChangedEventArgs>(nameof(TerrainFeatureListChanged), h => inner.TerrainFeatureListChanged += h, value);
            remove => router.Unsubscribe<TerrainFeatureListChangedEventArgs>(nameof(TerrainFeatureListChanged), h => inner.TerrainFeatureListChanged -= h, value);
        }

        public event EventHandler<FurnitureListChangedEventArgs> FurnitureListChanged
        {
            add => router.Subscribe<FurnitureListChangedEventArgs>(nameof(FurnitureListChanged), h => inner.FurnitureListChanged += h, value);
            remove => router.Unsubscribe<FurnitureListChangedEventArgs>(nameof(FurnitureListChanged), h => inner.FurnitureListChanged -= h, value);
        }
    }

    internal sealed class GatedSpecializedEvents : ISpecializedEvents
    {
        private readonly ISpecializedEvents inner;
        private readonly GatedEventRouter router;

        public GatedSpecializedEvents(ISpecializedEvents inner, GatedEventRouter router)
        {
            this.inner = inner;
            this.router = router;
        }

        public event EventHandler<LoadStageChangedEventArgs> LoadStageChanged
        {
            add => router.Subscribe<LoadStageChangedEventArgs>(nameof(LoadStageChanged), h => inner.LoadStageChanged += h, value);
            remove => router.Unsubscribe<LoadStageChangedEventArgs>(nameof(LoadStageChanged), h => inner.LoadStageChanged -= h, value);
        }

        public event EventHandler<UnvalidatedUpdateTickingEventArgs> UnvalidatedUpdateTicking
        {
            add => router.Subscribe<UnvalidatedUpdateTickingEventArgs>(nameof(UnvalidatedUpdateTicking), h => inner.UnvalidatedUpdateTicking += h, value);
            remove => router.Unsubscribe<UnvalidatedUpdateTickingEventArgs>(nameof(UnvalidatedUpdateTicking), h => inner.UnvalidatedUpdateTicking -= h, value);
        }

        public event EventHandler<UnvalidatedUpdateTickedEventArgs> UnvalidatedUpdateTicked
        {
            add => router.Subscribe<UnvalidatedUpdateTickedEventArgs>(nameof(UnvalidatedUpdateTicked), h => inner.UnvalidatedUpdateTicked += h, value);
            remove => router.Unsubscribe<UnvalidatedUpdateTickedEventArgs>(nameof(UnvalidatedUpdateTicked), h => inner.UnvalidatedUpdateTicked -= h, value);
        }
    }
}
