#nullable enable

using System;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public sealed class SongEnumerationBusyExceptionTests
{
    [Fact]
    public void DefaultConstructor_ShouldUseCanonicalMessageAndDeriveFromInvalidOperation()
    {
        var exception = new SongEnumerationBusyException();

        Assert.Equal("Song enumeration is already in progress.", exception.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public void MessageConstructor_ShouldPreserveSuppliedMessage()
    {
        var exception = new SongEnumerationBusyException("custom busy message");

        Assert.Equal("custom busy message", exception.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public void MessageAndInnerExceptionConstructor_ShouldPreserveBoth()
    {
        var inner = new InvalidOperationException("root cause");

        var exception = new SongEnumerationBusyException("wrapped busy", inner);

        Assert.Equal("wrapped busy", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public void CatchAsInvalidOperationException_ShouldMatchABusyEnumerationHandler()
    {
        // A handler that only catches InvalidOperationException must still classify
        // a busy enumeration without string-matching the message.
        InvalidOperationException? caught = null;
        try
        {
            throw new SongEnumerationBusyException();
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        Assert.IsType<SongEnumerationBusyException>(caught);
    }
}
