using System.Collections.Generic;
using System.Reflection;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

public class SmokeTests
{
    [Fact]
    public void library_assembly_loads()
        => Assembly.Load("CodeBrix.LilyScheme").Should().NotBeNull();

    [Fact]
    public void every_vendored_scheme_resource_arrives_without_carriage_returns()
    {
        //Arrange
        // The .scm files are embedded resources, so a CRLF working tree is baked into
        // the assembly and SHIPPED. A CR is whitespace between forms and is NOT
        // whitespace inside a string literal -- and the multi-line format-directive
        // literals of ice-9/format.scm are full of them, which makes format's parser run
        // off the end of its string and recurse through format-error until the process
        // dies with an uncatchable stack overflow. Nothing warns; the package is simply
        // broken for every consumer on every platform.
        //
        // .gitattributes pins these files to LF, but that governs a CHECKOUT and nothing
        // else. This fences the enforcement in SchemeBootstrap, which is what makes the
        // ARTIFACT correct however the bytes reached disk.
        //
        // The full manifest name is passed through deliberately: ReadVendoredSource
        // matches it exactly, so the sweep needs no guess about how MSBuild derived it.
        Assembly assembly = typeof(SchemeBootstrap).Assembly;
        List<string> offenders = new List<string>();

        //Act
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(".scm", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (SchemeBootstrap.ReadVendoredSource(name).IndexOf('\r') >= 0)
            {
                offenders.Add(name);
            }
        }

        //Assert
        offenders.Should().BeEmpty();
    }
}
