using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using Cysharp.Threading.Tasks;
using MVVMToolkit.DependencyInjection;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace MVVMToolkit
{
    public class UIRoot : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private UIDocument _uiDocument;

        public UIDocument UIDocument
        {
            get => _uiDocument;
            set
            {
                if (_uiDocument != value)
                {
                    value?.rootVisualElement.Add(Root);
                    _uiDocument?.rootVisualElement.Remove(Root);
                    _uiDocument = value;
                }
            }
        }

        public VisualElement Root { get; private set; }

        private readonly List<BaseView> _views = new();
        private readonly List<ViewModel> _viewModels = new();

        public async UniTask Initialize(StrongReferenceMessenger messenger, ServiceProvider serviceProvider)
        {
            if (Root is not null) throw new InvalidOperationException("Cannot initialize UIRoot multiple times");

            Root = new() { style = { flexGrow = 1f } };
            UIDocument = uiDocument;

            foreach (var viewModel in GetComponentsInChildren<ViewModel>())
            {
                _viewModels.Add(viewModel);
                serviceProvider.RegisterService(viewModel);
            }


            var views = GetComponentsInChildren<BaseView>();

            var stringTables = new HashSet<LocalizedStringTable>(views.Length);
            var assetTables = new HashSet<LocalizedAssetTable>(views.Length);

            foreach (var view in GetComponentsInChildren<BaseView>())
            {
                _views.Add(view);

                // We also collect all tables so we can await their load
                foreach (var stringTable in view.LocalizedStringTables)
                {
                    stringTables.Add(stringTable);
                }

                foreach (var assetTable in view.LocalizedAssetTables)
                {
                    assetTables.Add(assetTable);
                }
            }

            // First we await for localization settings to load
            await LocalizationSettings.InitializationOperation;


            // Then we await for each registered table to load
            foreach (var table in stringTables)
            {
                var handle = table.CurrentLoadingOperationHandle;
                if (!handle.IsValid()) handle = table.GetTableAsync();

                await handle;
            }

            foreach (var table in assetTables)
            {
                var handle = table.CurrentLoadingOperationHandle;
                if (!handle.IsValid()) handle = table.GetTableAsync();

                await handle;
            }


            // After all services have been registered we do dependency injection
            serviceProvider.Inject();

            // Now we resolve cross references for views and viewModels
            foreach (var view in _views) view.Attach(messenger, serviceProvider);
            foreach (var viewModel in _viewModels) viewModel.Attach(messenger, serviceProvider);


            var sortDict = new Dictionary<VisualElement, int>(_views.Count);

            foreach (var view in _views)
            {
                view.Initialize();
                if (view.SortLayer != 0)
                    sortDict.Add(view.RootVisualElement, view.SortLayer);
            }

            foreach (var view in _views)
            {
                var parent = view.ResolveParent() ?? Root;
                parent.Add(view.RootVisualElement);
                parent.Sort((x, y) =>
                {
                    sortDict.TryGetValue(x, out var xSort);
                    sortDict.TryGetValue(y, out var ySort);
                    return Comparer<int>.Default.Compare(xSort, ySort);
                });
            }
        }
    }
}