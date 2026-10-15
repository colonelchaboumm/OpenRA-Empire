// Test-only platform: normal engine, assets, simulation and TCP transport;
// GPU calls are discarded so several clients can run without a display driver.
using System;
using System.IO;
using System.IO.Compression;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Platforms.Default;

namespace OpenRA.Platforms.NetworkTest
{
	public sealed class NullPlatform : IPlatform
	{
		public IPlatformWindow CreateWindow(Size size, WindowMode mode, float scale, int vertices, int indices, int display, GLProfile profile) => new Window(size);
		public ISoundEngine CreateSound(string device) => new DummySoundEngine();
		public IFont CreateFont(byte[] data) => new DefaultPlatform().CreateFont(data);
	}
	sealed class Window(Size size) : IPlatformWindow
	{
		public IGraphicsContext Context { get; } = new Context();
		public Size NativeWindowSize => size;
		public Size EffectiveWindowSize => size;
		public Size SurfaceSize => size;
		public float NativeWindowScale => 1;
		public float EffectiveWindowScale => 1;
		public int DisplayCount => 1;
		public int CurrentDisplay => 0;
		public bool HasInputFocus => true;
		public bool IsSuspended => false;
		public GLProfile GLProfile => GLProfile.Modern;
		public GLProfile[] SupportedGLProfiles => [GLProfile.Modern];
		public event Action<float, float, float, float> OnWindowScaleChanged { add { } remove { } }
		public void PumpInput(IInputHandler input) { }
		public string GetClipboardText() => "";
		public bool SetClipboardText(string text) => false;
		public bool TryOpenUrl(string url) => false;
		public void GrabWindowMouseFocus() { }
		public void ReleaseWindowMouseFocus() { }
		public IHardwareCursor CreateHardwareCursor(string name, Size size, byte[] data, int2 hotspot, bool pixelDouble) => new Cursor();
		public void SetHardwareCursor(IHardwareCursor cursor) { }
		public void SetWindowTitle(string title) { }
		public void SetRelativeMouseMode(bool mode) { }
		public void SetScaleModifier(float scale) { }
		public void Dispose() { }
	}
	sealed class Cursor : IHardwareCursor { public void Dispose() { } }
	sealed class Context : IGraphicsContext
	{
		public string GLVersion => "Network test (no GPU)";
		public IVertexBuffer<T> CreateEmptyVertexBuffer<T>(int size) where T : struct => new Vertices<T>();
		public IVertexBuffer<T> CreateVertexBuffer<T>(T[] data, bool dynamic = true) where T : struct => new Vertices<T>();
		public T[] CreateVertices<T>(int size) where T : struct => new T[size];
		public IIndexBuffer CreateIndexBuffer(uint[] indices) => new Indices();
		public ITexture CreateTexture() => new Texture();
		public IFrameBuffer CreateFrameBuffer(Size size) => new FrameBuffer(size);
		public IFrameBuffer CreateFrameBuffer(Size size, Color color) => new FrameBuffer(size);
		public IShader CreateShader(IShaderBindings bindings) => new Shader();
		public void EnableScissor(int x, int y, int w, int h) { }
		public void DisableScissor() { }
		public void Present() { }
		public void DrawPrimitives(PrimitiveType type, int first, int count) { }
		public void DrawElements(int count, int offset) { }
		public void Clear() { }
		public void EnableDepthBuffer() { }
		public void DisableDepthBuffer() { }
		public void ClearDepthBuffer() { }
		public void SetBlendMode(BlendMode mode) { }
		public void SetVSyncEnabled(bool enabled) { }
		public void Dispose() { }
	}
	sealed class Vertices<T> : IVertexBuffer<T> where T : struct
	{
		public void Bind() { }
		public void SetData(T[] data, int length) { }
		public void SetData(ref T[] data, int length) { }
		public void SetData(T[] data, int offset, int start, int length) { }
		public void Dispose() { }
	}
	sealed class Indices : IIndexBuffer { public void Bind() { } public void Dispose() { } }
	sealed class Shader : IShader
	{
		public void SetBool(string name, bool value) { }
		public void SetVec(string name, float x) { }
		public void SetVec(string name, float x, float y) { }
		public void SetVec(string name, float x, float y, float z) { }
		public void SetVec(string name, ReadOnlyMemory<float> vec, int length) { }
		public void SetTexture(string name, ITexture texture) { }
		public void SetMatrix(string name, float[] matrix) { }
		public void PrepareRender() { }
		public void Bind() { }
	}
	sealed class Texture : ITexture
	{
		byte[] data = [];
		int length;
		bool compressed;
		public Size Size { get; private set; }
		public TextureScaleFilter ScaleFilter { get; set; }
		public void SetData(byte[] colors, int width, int height)
		{
			Size = new Size(width, height); length = colors.Length;
			// Real textures live on the GPU. Retaining every atlas uncompressed in each
			// headless client exhausts RAM in three-player tests. Preserve exact readback.
			compressed = length >= 65536;
			if (!compressed) { data = (byte[])colors.Clone(); return; }
			using var output = new MemoryStream();
			using (var stream = new DeflateStream(output, CompressionLevel.Fastest, true)) stream.Write(colors);
			data = output.ToArray();
		}
		public void SetSubData(byte[] colors, int x, int y, int width, int height)
		{
			var pixels = GetData();
			for (var row = 0; row < height; row++)
			{
				var offset = ((row + y) * Size.Width + x) * 4;
				Array.Copy(colors, offset, pixels, offset, width * 4);
			}
			SetData(pixels, Size.Width, Size.Height);
		}
		public void SetFloatData(float[] colors, int width, int height) { Size = new Size(width, height); }
		public void SetDataFromReadBuffer(Rectangle rect) => SetData(new byte[rect.Width * rect.Height * 4], rect.Width, rect.Height);
		public byte[] GetData()
		{
			if (!compressed) return (byte[])data.Clone();
			var pixels = new byte[length];
			using var input = new MemoryStream(data);
			using var stream = new DeflateStream(input, CompressionMode.Decompress);
			stream.ReadExactly(pixels);
			return pixels;
		}
		public void Dispose() { data = []; length = 0; compressed = false; }
	}
	sealed class FrameBuffer : IFrameBuffer
	{
		public ITexture Texture { get; }
		public FrameBuffer(Size size) { Texture = new Texture(); Texture.SetData(new byte[size.Width * size.Height * 4], size.Width, size.Height); }
		public void Bind() { }
		public void Unbind() { }
		public void EnableScissor(Rectangle rect) { }
		public void DisableScissor() { }
		public void Dispose() => Texture.Dispose();
	}
}
