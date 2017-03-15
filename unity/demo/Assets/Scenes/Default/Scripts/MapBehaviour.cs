﻿using System;
using System.Collections.Generic;
using Assets.Scenes.Default.Scripts.Animations;
using Assets.Scenes.Default.Scripts.Gestures;
using Assets.Scenes.Default.Scripts.Spaces;
using Assets.Scenes.Default.Scripts.Tiling;
using Assets.Scripts;
using TouchScript.Gestures.TransformGestures;
using UnityEngine;
using UtyMap.Unity;
using UtyMap.Unity.Data;
using UtyMap.Unity.Infrastructure.Config;
using UtyMap.Unity.Infrastructure.Primitives;

using Animator = UtyMap.Unity.Animations.Animator;
using Space = Assets.Scenes.Default.Scripts.Spaces.Space;

namespace Assets.Scenes.Default.Scripts
{
    internal class MapBehaviour: MonoBehaviour
    {
        #region User controlled settings

        public enum SpaceType { Orbit, Surface, Detail }

        public double StartLatitude = 52.53171;
        public double StartLongitude = 13.38721;
        public SpaceType StartSpace = SpaceType.Orbit;

        public Transform Planet;
        public Transform Surface;
        public Transform Pivot;
        public Camera Camera;

        public ScreenTransformGesture TwoFingerMoveGesture;
        public ScreenTransformGesture ManipulationGesture;

        public bool ShowState = true;

        #endregion

        private int _currentSpaceIndex;
        private List<Space> _spaces;
        private List<Animator> _animators;

        #region Unity lifecycle methods

        void Start()
        {
            var appManager = ApplicationManager.Instance;
            appManager.InitializeFramework(ConfigBuilder.GetDefault());

            var mapDataStore = appManager.GetService<IMapDataStore>();
            var stylesheet = appManager.GetService<Stylesheet>();
            var geoOrigin = new GeoCoordinate(StartLatitude, StartLongitude);
            _currentSpaceIndex = (int)StartSpace;

            // scaled radius of Earth in meters, approx. 1:1000
            const float planetRadius = 6371f;
            const float surfaceScale = 0.01f;
            const float detailScale = 1f;
            const float maxDistance = 3000;
            float aspect = Camera.aspect;

            _spaces = new List<Space>()
            {
                // Orbit
                new SphereSpace(new SphereTileController(mapDataStore, stylesheet, ElevationDataType.Flat, new Range<int>(1, 8), planetRadius),
                                new SphereGestureStrategy(TwoFingerMoveGesture, ManipulationGesture, planetRadius), Pivot, Camera, Planet),
                // Surface
                new SurfaceSpace(new SurfaceTileController(mapDataStore, stylesheet, ElevationDataType.Grid, new Range<int>(9, 15), geoOrigin, aspect, surfaceScale, maxDistance),
                                 new SurfaceGestureStrategy(TwoFingerMoveGesture, ManipulationGesture), Pivot, Camera,Surface),
                // Detail
                new SurfaceSpace(new SurfaceTileController(mapDataStore, stylesheet, ElevationDataType.Grid, new Range<int>(16, 16), geoOrigin, aspect, detailScale, maxDistance),
                                 new SurfaceGestureStrategy(TwoFingerMoveGesture, ManipulationGesture), Pivot, Camera,Surface)
            };

            _animators = new List<Animator>()
            {
                // Orbit
                new SphereAnimator(Pivot, Camera.transform, _spaces[0].TileController),
                // Surface
                new SurfaceAnimator(),
                // Detail
                new SurfaceAnimator()
            };

            OnTransition(null, _spaces[_currentSpaceIndex]);
        }

        void OnEnable()
        {
            TwoFingerMoveGesture.Transformed += TwoFingerTransformHandler;
            ManipulationGesture.Transformed += ManipulationTransformedHandler;
        }

        void OnDisable()
        {
            TwoFingerMoveGesture.Transformed -= TwoFingerTransformHandler;
            ManipulationGesture.Transformed -= ManipulationTransformedHandler;
        }

        void Update()
        {
            if (_animators[_currentSpaceIndex].IsRunningAnimation)
            {
                _animators[_currentSpaceIndex].Update(Time.deltaTime);
                return;
            }

            _spaces[_currentSpaceIndex].TileController.OnUpdate(_currentSpaceIndex == 0 ? Planet : Surface, 
                Camera.transform.localPosition, Pivot.rotation.eulerAngles);
        }

        void OnGUI()
        {
            if (ShowState)
            {
                var tileController = _spaces[_currentSpaceIndex].TileController;
                var labelText = String.Format("Position: {0}\nZoom: {1}",
                    tileController.Coordinate,
                    tileController.ZoomLevel);
                
                GUI.contentColor = Color.red;
                GUI.Label(new Rect(0, 0, Screen.width, Screen.height), labelText);
            }
        }

        #endregion

        #region Touch handles

        private void ManipulationTransformedHandler(object sender, EventArgs e)
        {
            _spaces[_currentSpaceIndex].GestureStrategy.OnManipulationTransform(Pivot, Camera.transform);
        }

        private void TwoFingerTransformHandler(object sender, EventArgs e)
        {
            _spaces[_currentSpaceIndex].GestureStrategy.OnTwoFingerTransform(Pivot, Camera.transform);
        }

        #endregion

        #region Space change logic

        /// <summary> Performs transition from one space to another. </summary>
        private void OnTransition(Space from, Space to)
        {
            // calculate "to"-space parameters
            var coordinate = from == null
                ? new GeoCoordinate(StartLatitude, StartLongitude)
                : from.TileController.Coordinate;

            var zoom = from != null && from.TileController.LodRange.Maximum > to.TileController.LodRange.Maximum
                ? to.TileController.LodRange.Maximum
                : to.TileController.LodRange.Minimum;

            // prepare scen for "to"-state by making transition
            if (from != null)
                from.Leave();
            to.Enter();
           
            // make an instant animation to given position
            _animators[_currentSpaceIndex].AnimateTo(coordinate, zoom, TimeSpan.Zero);
        }

        #endregion
    }
}