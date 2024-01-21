# AwaitFuscator

This is the AwaitFuscator; a .NET binary-to-binary obfuscator that translates your code into long chains of `await` expressions:

![](/assets/example.png)

For more details on how it works, check the [FAQ](#how-does-it-work) or read the [accompanied blog post](https://blog.washi.dev/posts/awaitfuscator).


## How To Build

This project depends on a few other projects.
Make sure you have all submodules cloned:

```sh
$ git clone --recursive https://github.com/Washi1337/AwaitFuscator.git
```

If you accidentally didn't clone the submodules, you can go to your repository directory and run the following instead:

```sh
$ git submodule update --init
```

Then, just compile using your favourite IDE like Visual Studio or JetBrains Rider, or run the following:

```sh
$ dotnet build
```

The binaries will then appear in `src/AwaitFuscator/bin`.


## How To Use

To awaitfuscate a program, simply run Awaitfuscator with the path of the binary to obfuscate:

```sh
$ AwaitFuscator [path]
```

If everything goes well (which is a big "if"), this will create a folder called `Obfuscated` in the parent directory of the input file containing the output.

For example:

```sh
$ AwaitFuscator /path/to/file.exe
```

will produce a file at `/path/to/Obfuscated/file.exe`.

## FAQ

### How does it work?

C# allows for custom awaiters to be defined on any type using custom `GetAwaiter` extension methods and custom awaiter types.
These awaiter types define a method called `GetResult` can contain any code you want.

Awaitfuscator locates all "awaitifiable" methods in the input binary, and creates for each statement a new awaiter with the original statement's code moved into its `GetResult` method.
Then, by defining custom `GetAwaiter` extension methods, it is possible to await the custom awaiters, and thus chain a bunch of awaiters together.
This effectively rewrites the entire method body as one long chain of awaits.

For more details, read the [accompanied blog post](https://blog.washi.dev/posts/awaitfuscator).


### Is the code in the output binary really hidden?

No. 
The original code is still more or less there, just slightly rewritten and scattered around the assembly in different places.
Awaitfuscator just plays a bunch of tricks that confuses decompilers a lot.

For more details, read the [accompanied blog post](https://blog.washi.dev/posts/awaitfuscator).


### Can I use it in my next product?

You could.
Not sure if it is a good idea though.


### Is it production-ready?

Probably not.


### Heeelp it...

- ... crashes,
- ... produces errors I don't understand,
- ... corrupts my files,

These are very likely to happen as this is more of a proof of concept rather than a finalized product.
Nonetheless, [bug reports](https://github.com/Washi1337/AwaitFuscator/issues/new) are appreciated :).
