using System;
using System.Collections.Generic;
using ApexPack;
using Xunit;

namespace ApexPack.Tests;

[ApexPackable]
[ApexUnion(0, typeof(CircleShape))]
[ApexUnion(1, typeof(RectangleShape))]
public abstract partial class Shape
{
    public string? Label { get; set; }
    public double Opacity { get; set; }
}

[ApexPackable]
public partial class CircleShape : Shape
{
    public double Radius { get; set; }
}

[ApexPackable]
public partial class RectangleShape : Shape
{
    public double Width { get; set; }
    public double Height { get; set; }
}

[ApexPackable]
[ApexUnion(0, typeof(JumpAction))]
[ApexUnion(1, typeof(AttackAction))]
public partial interface IAction
{
    string ActionName { get; set; }
}

[ApexPackable]
public partial class JumpAction : IAction
{
    public string ActionName { get; set; } = "Jump";
    public float JumpHeight { get; set; }
}

[ApexPackable]
public partial class AttackAction : IAction
{
    public string ActionName { get; set; } = "Attack";
    public int Damage { get; set; }
}

[ApexPackable]
public partial class CanvasScene
{
    public Shape? Background { get; set; }
    public Shape[]? Shapes { get; set; }
    public List<IAction>? ActionQueue { get; set; }
}

public class PolymorphismAndComboTests
{
    [Fact]
    public void AbstractClass_Inheritance_Roundtrip()
    {
        Shape shape = new CircleShape
        {
            Label = "Red Circle",
            Opacity = 0.85,
            Radius = 42.5
        };

        byte[] bytes = ApexPackSerializer.Serialize(shape);
        Shape restored = ApexPackSerializer.Deserialize<Shape>(bytes);

        Assert.NotNull(restored);
        var circle = Assert.IsType<CircleShape>(restored);
        Assert.Equal("Red Circle", circle.Label);
        Assert.Equal(0.85, circle.Opacity);
        Assert.Equal(42.5, circle.Radius);
    }

    [Fact]
    public void Interface_Polymorphism_Roundtrip()
    {
        IAction action = new AttackAction
        {
            ActionName = "Slash",
            Damage = 150
        };

        byte[] bytes = ApexPackSerializer.Serialize(action);
        IAction restored = ApexPackSerializer.Deserialize<IAction>(bytes);

        Assert.NotNull(restored);
        var attack = Assert.IsType<AttackAction>(restored);
        Assert.Equal("Slash", attack.ActionName);
        Assert.Equal(150, attack.Damage);
    }

    [Fact]
    public void ComplexCombinatorialScene_Roundtrip()
    {
        var scene = new CanvasScene
        {
            Background = new RectangleShape { Label = "Sky", Opacity = 1.0, Width = 1920, Height = 1080 },
            Shapes = new Shape[]
            {
                new CircleShape { Label = "Sun", Opacity = 0.9, Radius = 50 },
                new RectangleShape { Label = "Ground", Opacity = 1.0, Width = 1920, Height = 200 }
            },
            ActionQueue = new List<IAction>
            {
                new JumpAction { ActionName = "HeroJump", JumpHeight = 12.5f },
                new AttackAction { ActionName = "Fireball", Damage = 999 }
            }
        };

        byte[] bytes = ApexPackSerializer.Serialize(scene);
        var restored = ApexPackSerializer.Deserialize<CanvasScene>(bytes);

        Assert.NotNull(restored);
        Assert.NotNull(restored.Background);
        Assert.IsType<RectangleShape>(restored.Background);

        Assert.NotNull(restored.Shapes);
        Assert.Equal(2, restored.Shapes.Length);
        Assert.IsType<CircleShape>(restored.Shapes[0]);
        Assert.IsType<RectangleShape>(restored.Shapes[1]);

        Assert.NotNull(restored.ActionQueue);
        Assert.Equal(2, restored.ActionQueue.Count);
        Assert.IsType<JumpAction>(restored.ActionQueue[0]);
        Assert.IsType<AttackAction>(restored.ActionQueue[1]);
    }
}
