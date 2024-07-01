﻿using System.IO;
using System.Numerics;
using Content.Client.Administration.Managers;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._RMC14.Sprite;

public sealed class ContentSpriteSystem : EntitySystem
{
    [Dependency] private readonly IClientAdminManager _adminManager = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly ContentSpriteControl _control = new();

    public override void Initialize()
    {
        base.Initialize();

        IoCManager.Resolve<IUserInterfaceManager>().RootControl.AddChild(_control);
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(GetVerbs);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        IoCManager.Resolve<IUserInterfaceManager>().RootControl.RemoveChild(_control);
    }

    private void Export(EntityUid entity)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryComp(entity, out SpriteComponent? spriteComp))
            return;

        // Don't want to wait for engine pr
        var size = Vector2i.Zero;

        foreach (var layer in spriteComp.AllLayers)
        {
            if (!layer.Visible)
                continue;

            size = Vector2i.ComponentMax(size, layer.PixelSize);
        }

        // Stop asserts
        if (size.Equals(Vector2i.Zero))
            return;

        foreach (var direction in new[] { Direction.South, Direction.East, Direction.North, Direction.West })
        {
            var texture = _clyde.CreateRenderTarget(new Vector2i(size.X, size.Y), new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "export");

            _control.QueuedTextures.Enqueue((texture, direction, entity));
        }
    }

    private void GetVerbs(GetVerbsEvent<Verb> ev)
    {
        if (!_adminManager.IsAdmin())
            return;

        Verb verb = new()
        {
            Text = "Export entity as PNG",
            Category = VerbCategory.Debug,
            Act = () =>
            {
                Export(ev.Target);
            },
        };

        ev.Verbs.Add(verb);
    }

    private sealed class ContentSpriteControl : Control
    {
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly IResourceManager _resManager = default!;

        internal readonly Queue<(IRenderTexture Texture, Direction Direction, EntityUid Entity)> QueuedTextures = new();

        public ContentSpriteControl()
        {
            IoCManager.InjectDependencies(this);
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            while (QueuedTextures.TryDequeue(out var queued))
            {
                try
                {
                    if (!_entManager.TryGetComponent(queued.Entity, out MetaDataComponent? metadata))
                        continue;

                    var filename = metadata.EntityName;
                    var result = queued;

                    handle.RenderInRenderTarget(queued.Texture,
                        () =>
                        {
                            handle.DrawEntity(result.Entity,
                                result.Texture.Size / 2,
                                Vector2.One,
                                Angle.Zero,
                                overrideDirection: result.Direction);
                        },
                        Color.Transparent);

                    var directory = new ResPath("/Exports");

                    if (!_resManager.UserData.IsDir(directory))
                    {
                        _resManager.UserData.CreateDir(directory);
                    }

                    var fullFileName = directory / $"{filename}-{queued.Direction}-{queued.Entity}.png";

                    queued.Texture.CopyPixelsToMemory<Rgba32>(image =>
                    {
                        if (_resManager.UserData.Exists(fullFileName))
                        {
                            Logger.Info($"Found existing file {fullFileName} to replace.");
                            _resManager.UserData.Delete(fullFileName);
                        }

                        using var file = _resManager.UserData.Open(
                            fullFileName,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None
                        );
                        image.SaveAsPng(file);
                    });

                    Logger.Info($"Saved screenshot to {fullFileName}");
                }
                catch (Exception exc)
                {
                    queued.Texture.Dispose();

                    if (!string.IsNullOrEmpty(exc.StackTrace))
                        Logger.Fatal(exc.StackTrace);
                }
            }
        }
    }
}
