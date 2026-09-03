using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Profiles;

namespace Tappy.Core.Tests;

public sealed class ProfileSnapshotTests
{
    [Fact]
    public void ControllerDefaultsToThreeLayersWithoutImposingAFixedLimit()
    {
        var defaultController = ControllerProfile.Create(TestProfiles.Identity());
        var manyLayerController = ControllerProfile.Create(
            TestProfiles.Identity("many-session", "many-controller"), defaultLayerCount: 12);

        Assert.Equal(3, defaultController.Layers.Count);
        Assert.Equal(12, manyLayerController.Layers.Count);
        Assert.Equal("layer-12", manyLayerController.Layers[^1].Id);
    }

    [Fact]
    public void SnapshotIsDeepAndUnaffectedByEditableProfileMutation()
    {
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var controller = ControllerProfile.Create(TestProfiles.Identity(), [control]);
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            Name = "Original",
            PressAction = KeyboardActionDefinition.Hold("Ctrl", "A"),
            PressSequence = ControllerActionSequenceDefinition.Once("OSC",
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Osc,
                    Target = "127.0.0.1",
                    Amount = 8000,
                    Value = "/original"
                })
        });
        var editable = new TappyProfile { Name = "Before", Controllers = [controller] };

        var snapshot = editable.CreateSnapshot();
        editable.Name = "After";
        editable.Controllers[0].DisplayName = "Changed";
        editable.Controllers[0].Layers[0].Bindings[0].Name = "Changed";
        editable.Controllers[0].Layers[0].Bindings[0].PressAction.Keys.Clear();
        editable.Controllers[0].Layers[0].Bindings[0].PressSequence.Steps[0].Value = "/changed";
        editable.Controllers.Clear();

        Assert.Equal("Before", snapshot.Name);
        var frozenController = Assert.Single(snapshot.Controllers);
        Assert.Equal("Test controller", frozenController.DisplayName);
        var frozenBinding = Assert.Single(frozenController.Layers[0].Bindings);
        Assert.Equal("Original", frozenBinding.Name);
        Assert.Equal(["CTRL", "A"], frozenBinding.PressAction.Keys.Select(key => key.Value));
        Assert.Equal("/original", Assert.Single(frozenBinding.PressSequence.Steps).Value);
    }

    [Fact]
    public void ProfileRoundTripsSeparateSourceModesAndMoreThanOneHundredControls()
    {
        var controls = Enumerable.Range(1, 128)
            .Select(index => ControlId.Create("test-provider", $"usage-{index:D3}"))
            .ToArray();
        var controller = ControllerProfile.Create(TestProfiles.Identity(), controls, defaultLayerCount: 7);
        controller.SourceMode.Requested = RequestedSourceMode.Exclusive;
        controller.SourceMode.Effective = EffectiveSourceMode.NeedsAttention;
        controller.SourceMode.Status = "Exclusive backend unavailable; failed open";
        controller.Layout = ControllerLayoutDefinition.CreateGrid(controls, columns: 12);
        controller.Layers[0].Bindings.AddRange(controls.Select((control, index) => new ControlBindingDefinition
        {
            ControlId = control,
            Name = $"Control {index + 1}",
            PressAction = KeyboardActionDefinition.Tap($"F{index % 24 + 1}")
        }));
        var serializer = new ProfileSerializer();

        var json = serializer.Serialize(new TappyProfile { Name = "Large", Controllers = [controller] });
        var loaded = serializer.Deserialize(json);
        var loadedController = Assert.Single(loaded.Controllers);

        Assert.Equal("Large", loaded.Name);
        Assert.Equal(7, loadedController.Layers.Count);
        Assert.Equal(128, loadedController.Layers[0].Bindings.Count);
        Assert.Equal(11, loadedController.Layout.Rows.Count);
        Assert.Equal(RequestedSourceMode.Exclusive, loadedController.SourceMode.Requested);
        Assert.Equal(EffectiveSourceMode.NeedsAttention, loadedController.SourceMode.Effective);
        Assert.Equal(controller.Identity.PersistentId, loadedController.Identity.PersistentId);
        Assert.Equal(controller.Identity.Confidence, loadedController.Identity.Confidence);
        var reconnectedIdentity = new ControllerIdentity(
            new ControllerSessionId("session-after-replug"),
            controller.Identity.PersistentId,
            controller.Identity.Confidence,
            controller.Identity.DisplayName,
            controller.Identity.ProviderId,
            controller.Identity.VendorId,
            controller.Identity.ProductId,
            controller.Identity.UsagePage,
            controller.Identity.Usage);
        Assert.Same(loadedController, loaded.FindController(reconnectedIdentity));
        Assert.Contains("test-provider:usage-128", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SameControlIdCanHaveIsolatedMappingsOnTwoControllers()
    {
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var first = ControllerProfile.Create(TestProfiles.Identity("one-session", "one-controller"), [control]);
        var second = ControllerProfile.Create(TestProfiles.Identity("two-session", "two-controller"), [control]);
        first.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            PressAction = KeyboardActionDefinition.Tap("F23")
        });
        second.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            PressAction = KeyboardActionDefinition.Tap("F24")
        });

        var snapshot = new TappyProfile { Controllers = [first, second] }.CreateSnapshot();

        Assert.Equal("F23", snapshot.Controllers[0].Layers[0].FindBinding(control)?.PressAction.Keys[0].Value);
        Assert.Equal("F24", snapshot.Controllers[1].Layers[0].FindBinding(control)?.PressAction.Keys[0].Value);
        Assert.NotEqual(snapshot.Controllers[0].Identity.PersistentId,
            snapshot.Controllers[1].Identity.PersistentId);
    }

    [Fact]
    public void SerializerReturnsIndependentSnapshotsOnEveryLoad()
    {
        var controller = ControllerProfile.Create(TestProfiles.Identity());
        var serializer = new ProfileSerializer();
        var json = serializer.Serialize(new TappyProfile { Controllers = [controller] });

        var first = serializer.Deserialize(json);
        var editable = first.ToEditableProfile();
        editable.Controllers[0].Layers.Add(InputLayerDefinition.Create(99));
        var second = serializer.Deserialize(json);

        Assert.Equal(3, first.Controllers[0].Layers.Count);
        Assert.Equal(4, editable.Controllers[0].Layers.Count);
        Assert.Equal(3, second.Controllers[0].Layers.Count);
    }

    [Fact]
    public void SerializerRejectsProfilesFromANewerSchema()
    {
        var serializer = new ProfileSerializer();
        var futureSchema = TappyProfile.CurrentSchemaVersion + 1;
        var json = $$"""
            {
              "schemaVersion": {{futureSchema}},
              "name": "Future"
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));

        Assert.Contains(futureSchema.ToString(), exception.Message, StringComparison.Ordinal);
    }
}
