﻿using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace D2dControl {
    public abstract class D2dControl : System.Windows.Controls.Image {
        // - field -----------------------------------------------------------------------

        private SharpDX.Direct3D11.Device device         ;
        private Texture2D                 renderTarget   ;
        private Dx11ImageSource           d3DSurface     ;
        private RenderTarget              d2DRenderTarget;
        private SharpDX.Direct2D1.Factory d2DFactory     ;

        private readonly Stopwatch renderTimer = new Stopwatch();
        protected ResourceCache resCache = new ResourceCache();
        private Queue<int> frameCountHist = new Queue<int>();

        // - property --------------------------------------------------------------------

        public static bool IsInDesignMode
        {
            get {
                var prop = DesignerProperties.IsInDesignModeProperty;
                var isDesignMode = (bool)DependencyPropertyDescriptor.FromProperty(prop, typeof(FrameworkElement)).Metadata.DefaultValue;
                return isDesignMode;
            }
        }

        public float FrameRate { get; set; } = 60F;

        // - public methods --------------------------------------------------------------

        public D2dControl() {
            base.Loaded   += Window_Loaded;
            base.Unloaded += Window_Closing;

            base.Stretch = System.Windows.Media.Stretch.Fill;
        }

        public abstract void Render( RenderTarget target );

        // - event handler ---------------------------------------------------------------

        private void Window_Loaded( object sender, RoutedEventArgs e ) {
            if ( D2dControl.IsInDesignMode ) {
                return;
            }

            StartD3D();
            StartRendering();
        }

        private void Window_Closing( object sender, RoutedEventArgs e ) {
            if ( D2dControl.IsInDesignMode ) {
                return;
            }

            StopRendering();
            EndD3D();
        }

        private void OnRendering( object sender, EventArgs e ) {
            if ( !renderTimer.IsRunning ) 
                return;

            PrepareAndCallRender();
            
            // NOTE:
            // Reloaded: Modified here to lessen CPU usage.
            d3DSurface.InvalidateD3DImage();
            SleepFrameRate();
        }

        protected override void OnRenderSizeChanged( SizeChangedInfo sizeInfo ) {
            CreateAndBindTargets();
            base.OnRenderSizeChanged( sizeInfo );
        }

        private void OnIsFrontBufferAvailableChanged( object sender, DependencyPropertyChangedEventArgs e ) {
            if ( d3DSurface.IsFrontBufferAvailable ) {
                StartRendering();
            } else {
                StopRendering();
            }
        }

        // - private methods -------------------------------------------------------------

        /// <summary>
        /// Sleeps and then spins a set amount of time to match the target framerate.
        /// </summary>
        private void SleepFrameRate()
        {
            // Calculate time for 1 frame.
            float sleepMilliseconds = 1000F / FrameRate;

            // Calculate time to Thread.Sleep (round down to nearest millisecond)
            int threadSleepMilliseconds = (int)sleepMilliseconds;

            // Go down another millisecond if not 0.
            if (threadSleepMilliseconds >= 3) { threadSleepMilliseconds -= 3; }

            // Sleep the thread.
            Thread.Sleep(threadSleepMilliseconds);

            // Stall with while loop
            while (renderTimer.Elapsed.TotalMilliseconds < sleepMilliseconds)
            { }

            // Reset stopwatch.
            renderTimer.Restart();
        }

        private void StartD3D() {
            device = new SharpDX.Direct3D11.Device( DriverType.Hardware, DeviceCreationFlags.BgraSupport );

            d3DSurface = new Dx11ImageSource();
            d3DSurface.IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged;

            CreateAndBindTargets();

            base.Source = d3DSurface;
        }

        private void EndD3D() {
            d3DSurface.IsFrontBufferAvailableChanged -= OnIsFrontBufferAvailableChanged;
            base.Source = null;

            Disposer.SafeDispose( ref d2DRenderTarget );
            Disposer.SafeDispose( ref d2DFactory );
            Disposer.SafeDispose( ref d3DSurface );
            Disposer.SafeDispose( ref renderTarget );
            Disposer.SafeDispose( ref device );
        }

        private void CreateAndBindTargets() {
            d3DSurface.SetRenderTarget( null );

            Disposer.SafeDispose( ref d2DRenderTarget );
            Disposer.SafeDispose( ref d2DFactory );
            Disposer.SafeDispose( ref renderTarget );

            var width  = Math.Max((int)ActualWidth , 100);
            var height = Math.Max((int)ActualHeight, 100);

            var renderDesc = new Texture2DDescription {
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                Format = Format.B8G8R8A8_UNorm,
                Width = width,
                Height = height,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                OptionFlags = ResourceOptionFlags.Shared,
                CpuAccessFlags = CpuAccessFlags.None,
                ArraySize = 1
            };

            renderTarget = new Texture2D( device, renderDesc );

            var surface = renderTarget.QueryInterface<Surface>();

            d2DFactory = new SharpDX.Direct2D1.Factory();
            var rtp = new RenderTargetProperties(new PixelFormat(Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied));
            d2DRenderTarget = new RenderTarget( d2DFactory, surface, rtp );
            resCache.RenderTarget = d2DRenderTarget;

            d3DSurface.SetRenderTarget( renderTarget );

            device.ImmediateContext.Rasterizer.SetViewport( 0, 0, width, height, 0.0f, 1.0f );
        }

        private void StartRendering() {
            if ( renderTimer.IsRunning ) {
                return;
            }

            System.Windows.Media.CompositionTarget.Rendering += OnRendering;
            renderTimer.Start();
        }

        private void StopRendering() {
            if ( !renderTimer.IsRunning ) {
                return;
            }

            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
            renderTimer.Stop();
        }

        private void PrepareAndCallRender() {
            if ( device == null ) {
                return;
            }

            d2DRenderTarget.BeginDraw();
            Render( d2DRenderTarget );
            d2DRenderTarget.EndDraw();

            device.ImmediateContext.Flush();
        }
    }
}
