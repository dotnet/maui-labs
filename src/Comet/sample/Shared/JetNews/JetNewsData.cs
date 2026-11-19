#nullable enable
using System.Collections.Generic;

namespace CometSamples.JetNews
{
	/// <summary>Verbatim port of the gold data (data/posts/impl/PostsData.kt) — five posts,
	/// full paragraph + markup content, same feed composition. Image ids are bundled
	/// resource names (post_N / post_N_thumb).</summary>
	public static class JetNewsData
	{
		static IReadOnlyList<T> L<T>(params T[] items) => items;














		/**
		 * Define hardcoded posts to avoid handling any non-ui operations.
		 */

		public static readonly PostAuthor pietro = new PostAuthor("Pietro Maggi", "https://medium.com/@pmaggi");
		public static readonly PostAuthor manuel = new PostAuthor("Manuel Vivo", "https://medium.com/@manuelvicnt");
		public static readonly PostAuthor florina = new PostAuthor(
		    "Florina Muntenescu",
		    "https://medium.com/@florina.muntenescu"
		);
		public static readonly PostAuthor jose = new PostAuthor("Jose Alcérreca", "https://medium.com/@JoseAlcerreca");

		public static readonly PostAuthor androidstudioteam = new PostAuthor("Android Studio Team", "https://twitter.com/androidstudio");

		public static readonly Publication ThePublication = new Publication(
		    "Android Developers",
		    "https://cdn-images-1.medium.com/max/258/1*u7oZc2_5mrkcFaxkXEyfYA@2x.png"
		);
		public static readonly IReadOnlyList<Paragraph> paragraphsPost1 = L(
		    new Paragraph(
		        ParagraphType.Text,
		        "Working to make our Android application more modular, I ended up with a sample that included a set of on-demand features grouped inside a folder:"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Pretty standard setup, all the on-demand modules, inside a “features” folder; clean."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "These modules are included in the settings.gradle file as:"
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "include ':app'\ninclude ':features:module1'\ninclude ':features:module2'\ninclude ':features:module3'\ninclude ':features:module4'"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "These setup works nicely with a single “minor” issue: an empty module named features in the Android view in Android Studio:"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "I can live with that, but I would much prefer to remove that empty module from my project!"
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "If you cannot remove it, just rename it!"
		    ),

		    new Paragraph(
		        ParagraphType.Text,
		        "At I/O I was lucky enough to attend the “Android Studio: Tips and Tricks” talk where Ivan Gravilovic, from Google, shared some amazing tips. One of these was a possible solution for my problem: setting a custom path for my modules.",
		        L(
		            new Markup(
		                MarkupType.Italic,
		                41,
		                72
		            )
		        )
		    ),

		    new Paragraph(
		        ParagraphType.Text,
		        "In this particular case our settings.gradle becomes:",
		        L(new Markup(MarkupType.Code, 28, 43))
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        """
		        include ':app'
		        include ':module1'
		        include ':module1'
		        include ':module1'
		        include ':module1'
		        """
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        """
		        // Set a custom path for the four features modules.
		        // This avoid to have an empty "features" module in  Android Studio.
		        project(":module1").projectDir=new File(rootDir, "features/module1")
		        project(":module2").projectDir=new File(rootDir, "features/module2")
		        project(":module3").projectDir=new File(rootDir, "features/module3")
		        project(":module4").projectDir=new File(rootDir, "features/module4")
		        """
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "And the layout in Android Studio is now:"
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Conclusion"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "As the title says, this is really a small thing, but it helps keep my project in order and it shows how a small Gradle configuration can help keep your project tidy."
		    ),
		    new Paragraph(
		        ParagraphType.Quote,
		        "You can find this update in the latest version of the on-demand modules codelab.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                54,
		                79,
		                "https://codelabs.developers.google.com/codelabs/on-demand-dynamic-delivery/index.html"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Resources"
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Android Studio: Tips and Tricks (Google I/O’19)",
		        L(
		            new Markup(
		                MarkupType.Link,
		                0,
		                47,
		                "https://www.youtube.com/watch?v=ihF-PwDfRZ4&list=PLWz5rJ2EKKc9FfSQIRXEWyWpHD6TtwxMM&index=32&t=0s"
		            )
		        )
		    ),

		    new Paragraph(
		        ParagraphType.Bullet,
		        "On Demand module codelab",
		        L(
		            new Markup(
		                MarkupType.Link,
		                0,
		                24,
		                "https://codelabs.developers.google.com/codelabs/on-demand-dynamic-delivery/index.html"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Patchwork Plaid — A modularization story",
		        L(
		            new Markup(
		                MarkupType.Link,
		                0,
		                40,
		                "https://medium.com/androiddevelopers/a-patchwork-plaid-monolith-to-modularized-app-60235d9f212e"
		            )
		        )
		    )
		);

		public static readonly IReadOnlyList<Paragraph> paragraphsPost2 = L(
		    new Paragraph(
		        ParagraphType.Text,
		        "Dagger is a popular Dependency Injection framework commonly used in Android. It provides fully static and compile-time dependencies addressing many of the development and performance issues that have reflection-based solutions.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                0,
		                6,
		                "https://dagger.dev/"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "This month, a new tutorial was released to help you better understand how it works. This article focuses on using Dagger with Kotlin, including best practices to optimize your build time and gotchas you might encounter.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                14,
		                26,
		                "https://dagger.dev/tutorial/"
		            ),
		            new Markup(MarkupType.Bold, 114, 132),
		            new Markup(MarkupType.Bold, 144, 159),
		            new Markup(MarkupType.Bold, 191, 198)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Dagger is implemented using Java’s annotations model and annotations in Kotlin are not always directly parallel with how equivalent Java code would be written. This post will highlight areas where they differ and how you can use Dagger with Kotlin without having a headache."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "This post was inspired by some of the suggestions in this Dagger issue that goes through best practices and pain points of Dagger in Kotlin. Thanks to all of the contributors that commented there!",
		        L(
		            new Markup(
		                MarkupType.Link,
		                58,
		                70,
		                "https://github.com/google/dagger/issues/900"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "kapt build improvements"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "To improve your build time, Dagger added support for gradle’s incremental annotation processing in v2.18! This is enabled by default in Dagger v2.24. In case you’re using a lower version, you need to add a few lines of code (as shown below) if you want to benefit from it.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                99,
		                104,
		                "https://github.com/google/dagger/releases/tag/dagger-2.18"
		            ),
		            new Markup(
		                MarkupType.Link,
		                143,
		                148,
		                "https://github.com/google/dagger/releases/tag/dagger-2.24"
		            ),
		            new Markup(MarkupType.Bold, 53, 95)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Also, you can tell Dagger not to format the generated code. This option was added in Dagger v2.18 and it’s the default behavior (doesn’t generate formatted code) in v2.23. If you’re using a lower version, disable code formatting to improve your build time (see code below).",
		        L(
		            new Markup(
		                MarkupType.Link,
		                92,
		                97,
		                "https://github.com/google/dagger/releases/tag/dagger-2.18"
		            ),
		            new Markup(
		                MarkupType.Link,
		                165,
		                170,
		                "https://github.com/google/dagger/releases/tag/dagger-2.23"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Include these compiler arguments in your build.gradle file to make Dagger more performant at build time:",
		        L(new Markup(MarkupType.Code, 41, 53))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Alternatively, if you use Kotlin DSL script files, include them like this in the build.gradle.kts file of the modules that use Dagger:",
		        L(new Markup(MarkupType.Code, 81, 97))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Qualifiers for field attributes"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "",
		        L(new Markup(MarkupType.Link, 0, 0))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "When an annotation is placed on a property in Kotlin, it’s not clear whether Java will see that annotation on the field of the property or the method for that property. Setting the field: prefix on the annotation ensures that the qualifier ends up in the right place (See documentation for more details).",
		        L(
		            new Markup(MarkupType.Code, 181, 187),
		            new Markup(
		                MarkupType.Link,
		                268,
		                285,
		                "http://frogermcs.github.io/dependency-injection-with-dagger-2-custom-scopes/"
		            ),
		            new Markup(MarkupType.Italic, 114, 119),
		            new Markup(MarkupType.Italic, 143, 149)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "✅ The way to apply qualifiers on an injected field is:"
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "@Inject @field:MinimumBalance lateinit var minimumBalance: BigDecimal",
		        L(new Markup(MarkupType.Bold, 8, 29))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "❌ As opposed to:"
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        """
		        @Inject @MinimumBalance lateinit var minimumBalance: BigDecimal 
		        // @MinimumBalance is ignored!
		        """,
		        L(new Markup(MarkupType.Bold, 65, 95))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Forgetting to add field: could lead to injecting the wrong object if there’s an unqualified instance of that type available in the Dagger graph.",
		        L(new Markup(MarkupType.Code, 18, 24))
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Static @Provides functions optimization"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Dagger’s generated code will be more performant if @Provides methods are static. To achieve this in Kotlin, use a Kotlin object instead of a class and annotate your methods with @JvmStatic. This is a best practice that you should follow as much as possible.",
		        L(
		            new Markup(MarkupType.Code, 51, 60),
		            new Markup(MarkupType.Code, 73, 79),
		            new Markup(MarkupType.Code, 121, 127),
		            new Markup(MarkupType.Code, 141, 146),
		            new Markup(MarkupType.Code, 178, 188),
		            new Markup(MarkupType.Bold, 200, 213),
		            new Markup(MarkupType.Italic, 200, 213)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "In case you need an abstract method, you’ll need to add the @JvmStatic method to a companion object and annotate it with @Module too.",
		        L(
		            new Markup(MarkupType.Code, 60, 70),
		            new Markup(MarkupType.Code, 121, 128)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Alternatively, you can extract the object module out and include it in the abstract one:"
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Injecting Generics"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Kotlin compiles generics with wildcards to make Kotlin APIs work with Java. These are generated when a type appears as a parameter (more info here) or as fields. For example, a Kotlin List<Foo> parameter shows up as List<? super Foo> in Java.",
		        L(
		            new Markup(MarkupType.Code, 184, 193),
		            new Markup(MarkupType.Code, 216, 233),
		            new Markup(
		                MarkupType.Link,
		                132,
		                146,
		                "https://kotlinlang.org/docs/reference/java-to-kotlin-interop.html#variant-generics"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "This causes problems with Dagger because it expects an exact (aka invariant) type match. Using @JvmSuppressWildcards will ensure that Dagger sees the type without wildcards.",
		        L(
		            new Markup(MarkupType.Code, 95, 116),
		            new Markup(
		                MarkupType.Link,
		                66,
		                75,
		                "https://en.wikipedia.org/wiki/Class_invariant"
		            ),
		            new Markup(
		                MarkupType.Link,
		                96,
		                116,
		                "https://kotlinlang.org/api/latest/jvm/stdlib/kotlin.jvm/-jvm-suppress-wildcards/index.html"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "This is a common issue when you inject collections using Dagger’s multibinding feature, for example:",
		        L(
		            new Markup(
		                MarkupType.Link,
		                57,
		                86,
		                "https://dagger.dev/multibindings.html"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        """
		        class MyVMFactory @Inject constructor(
		          private val vmMap: Map<String, @JvmSuppressWildcards Provider<ViewModel>>
		        ) { 
		            ... 
		        }
		        """,
		        L(new Markup(MarkupType.Bold, 72, 93))
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Inline method bodies"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Dagger determines the types that are configured by @Provides methods by inspecting the return type. Specifying the return type in Kotlin functions is optional and even the IDE sometimes encourages you to refactor your code to have inline method bodies that hide the return type declaration.",
		        L(new Markup(MarkupType.Code, 51, 60))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "This can lead to bugs if the inferred type is different from the one you meant. Let’s see some examples:"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "If you want to add a specific type to the graph, inlining works as expected. See the different ways to do the same in Kotlin:"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "If you want to provide an implementation of an interface, then you must explicitly specify the return type. Not doing it can lead to problems and bugs:"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Dagger mostly works with Kotlin out of the box. However, you have to watch out for a few things just to make sure you’re doing what you really mean to do: @field: for qualifiers on field attributes, inline method bodies, and @JvmSuppressWildcards when injecting collections.",
		        L(
		            new Markup(MarkupType.Code, 155, 162),
		            new Markup(MarkupType.Code, 225, 246)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Dagger optimizations come with no cost, add them and follow best practices to improve your build time: enabling incremental annotation processing, disabling formatting and using static @Provides methods in your Dagger modules.",
		        L(
		            new Markup(
		                MarkupType.Code,
		                185,
		                194
		            )
		        )
		    )
		);

		public static readonly IReadOnlyList<Paragraph> paragraphsPost3 = L(
		    new Paragraph(
		        ParagraphType.Text,
		        "Learn how to get started converting Java Programming Language code to Kotlin, making it more idiomatic and avoid common pitfalls, by following our new Refactoring to Kotlin codelab, available in English \uD83C\uDDEC\uD83C\uDDE7, Chinese \uD83C\uDDE8\uD83C\uDDF3 and Brazilian Portuguese \uD83C\uDDE7\uD83C\uDDF7.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                151,
		                172,
		                "https://codelabs.developers.google.com/codelabs/java-to-kotlin/#0"
		            ),
		            new Markup(
		                MarkupType.Link,
		                209,
		                216,
		                "https://clmirror.storage.googleapis.com/codelabs/java-to-kotlin-zh/index.html#0"
		            ),
		            new Markup(
		                MarkupType.Link,
		                226,
		                246,
		                "https://codelabs.developers.google.com/codelabs/java-to-kotlin-pt-br/#0"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "When you first get started writing Kotlin code, you tend to follow Java Programming Language idioms. The automatic converter, part of both Android Studio and Intellij IDEA, can do a pretty good job of automatically refactoring your code, but sometimes, it needs a little help. This is where our new Refactoring to Kotlin codelab comes in.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                105,
		                124,
		                "https://www.jetbrains.com/help/idea/converting-a-java-file-to-kotlin-file.html"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "We’ll take two classes (a User and a Repository) in Java Programming Language and convert them to Kotlin, check out what the automatic converter did and why. Then we go to the next level — make it idiomatic, teaching best practices and useful tips along the way.",
		        L(
		            new Markup(MarkupType.Code, 26, 30),
		            new Markup(MarkupType.Code, 37, 47)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "The Refactoring to Kotlin codelab starts with basic topics — understand how nullability is declared in Kotlin, what types of equality are defined or how to best handle classes whose role is just to hold data. We then continue with how to handle static fields and functions in Kotlin and how to apply the Singleton pattern, with the help of one handy keyword: object. We’ll see how Kotlin helps us model our classes better, how it differentiates between a property of a class and an action the class can do. Finally, we’ll learn how to execute code only in the context of a specific object with the scope functions.",
		        L(
		            new Markup(MarkupType.Code, 245, 251),
		            new Markup(MarkupType.Code, 359, 365),
		            new Markup(
		                MarkupType.Link,
		                4,
		                25,
		                "https://codelabs.developers.google.com/codelabs/java-to-kotlin/#0"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Thanks to Walmyr Carvalho and Nelson Glauber for translating the codelab in Brazilian Portuguese!",
		        L(
		            new Markup(
		                MarkupType.Link,
		                21,
		                42,
		                "https://codelabs.developers.google.com/codelabs/java-to-kotlin/#0"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "",
		        L(
		            new Markup(
		                MarkupType.Link,
		                76,
		                96,
		                "https://codelabs.developers.google.com/codelabs/java-to-kotlin-pt-br/#0"
		            )
		        )
		    )
		);

		public static readonly IReadOnlyList<Paragraph> paragraphsPost4 = L(
		    new Paragraph(
		        ParagraphType.Text,
		        "TL;DR: Expose resource IDs from ViewModels to avoid showing obsolete data."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "In a ViewModel, if you’re exposing data coming from resources (strings, drawables, colors…), you have to take into account that ViewModel objects ignore configuration changes such as locale changes. When the user changes their locale, activities are recreated but the ViewModel objects are not.",
		        L(
		            new Markup(
		                MarkupType.Bold,
		                183,
		                197
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "AndroidViewModel is a subclass of ViewModel that is aware of the Application context. However, having access to a context can be dangerous if you’re not observing or reacting to the lifecycle of that context. The recommended practice is to avoid dealing with objects that have a lifecycle in ViewModels.",
		        L(
		            new Markup(MarkupType.Code, 0, 16),
		            new Markup(MarkupType.Code, 34, 43),
		            new Markup(MarkupType.Bold, 209, 303)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Let’s look at an example based on this issue in the tracker: Updating ViewModel on system locale change.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                61,
		                103,
		                "https://issuetracker.google.com/issues/111961971"
		            ),
		            new Markup(MarkupType.Italic, 61, 104)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "The problem is that the string is resolved in the constructor only once. If there’s a locale change, the ViewModel won’t be recreated. This will result in our app showing obsolete data and therefore being only partially localized.",
		        L(new Markup(MarkupType.Bold, 73, 133))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "As Sergey points out in the comments to the issue, the recommended approach is to expose the ID of the resource you want to load and do so in the view. As the view (activity, fragment, etc.) is lifecycle-aware it will be recreated after a configuration change so the resource will be reloaded correctly.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                3,
		                9,
		                "https://twitter.com/ZelenetS"
		            ),
		            new Markup(
		                MarkupType.Link,
		                28,
		                36,
		                "https://issuetracker.google.com/issues/111961971#comment2"
		            ),
		            new Markup(MarkupType.Bold, 82, 150)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Even if you don’t plan to localize your app, it makes testing much easier and cleans up your ViewModel objects so there’s no reason not to future-proof."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "We fixed this issue in the android-architecture repository in the Java and Kotlin branches and we offloaded resource loading to the Data Binding layout.",
		        L(
		            new Markup(
		                MarkupType.Link,
		                66,
		                70,
		                "https://github.com/googlesamples/android-architecture/pull/631"
		            ),
		            new Markup(
		                MarkupType.Link,
		                75,
		                81,
		                "https://github.com/googlesamples/android-architecture/pull/635"
		            ),
		            new Markup(
		                MarkupType.Link,
		                128,
		                151,
		                "https://github.com/googlesamples/android-architecture/pull/635/files#diff-7eb5d85ec3ea4e05ecddb7dc8ae20aa1R62"
		            )
		        )
		    )
		);

		public static readonly IReadOnlyList<Paragraph> paragraphsPost5 = L(
		    new Paragraph(
		        ParagraphType.Text,
		        "Working with collections is a common task and the Kotlin Standard Library offers many great utility functions. It also offers two ways of working with collections based on how they’re evaluated: eagerly — with Collections, and lazily — with Sequences. Continue reading to find out what’s the difference between the two, which one you should use and when, and what the performance implications of each are.",
		        L(
		            new Markup(MarkupType.Code, 210, 220),
		            new Markup(MarkupType.Code, 241, 249),
		            new Markup(
		                MarkupType.Link,
		                210,
		                221,
		                "https://kotlinlang.org/api/latest/jvm/stdlib/kotlin.collections/index.html"
		            ),
		            new Markup(
		                MarkupType.Link,
		                241,
		                250,
		                "https://kotlinlang.org/api/latest/jvm/stdlib/kotlin.sequences/index.html"
		            ),
		            new Markup(MarkupType.Bold, 130, 134),
		            new Markup(MarkupType.Bold, 195, 202),
		            new Markup(MarkupType.Bold, 227, 233),
		            new Markup(MarkupType.Italic, 130, 134)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Collections vs sequences"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "The difference between eager and lazy evaluation lies in when each transformation on the collection is performed.",
		        L(
		            new Markup(
		                MarkupType.Italic,
		                57,
		                61
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Collections are eagerly evaluated — each operation is performed when it’s called and the result of the operation is stored in a new collection. The transformations on collections are inline functions. For example, looking at how map is implemented, we can see that it’s an inline function, that creates a new ArrayList:",
		        L(
		            new Markup(MarkupType.Code, 229, 232),
		            new Markup(MarkupType.Code, 273, 279),
		            new Markup(MarkupType.Code, 309, 318),
		            new Markup(
		                MarkupType.Link,
		                183,
		                199,
		                "https://kotlinlang.org/docs/reference/inline-functions.html"
		            ),
		            new Markup(
		                MarkupType.Link,
		                229,
		                232,
		                "https://github.com/JetBrains/kotlin/blob/master/libraries/stdlib/common/src/generated/_Collections.kt#L1312"
		            ),
		            new Markup(MarkupType.Bold, 0, 12),
		            new Markup(MarkupType.Italic, 16, 23)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "public inline fun <T, R> Iterable<T>.map(transform: (T) -> R): List<R> {\n  return mapTo(ArrayList<R>(collectionSizeOrDefault(10)), transform)\n}",
		        L(
		            new Markup(MarkupType.Bold, 7, 13),
		            new Markup(MarkupType.Bold, 88, 97)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Sequences are lazily evaluated. They have two types of operations: intermediate and terminal. Intermediate operations are not performed on the spot; they’re just stored. Only when a terminal operation is called, the intermediate operations are triggered on each element in a row and finally, the terminal operation is applied. Intermediate operations (like map, distinct, groupBy etc) return another sequence whereas terminal operations (like first, toList, count etc) don’t.",
		        L(
		            new Markup(MarkupType.Code, 357, 360),
		            new Markup(MarkupType.Code, 362, 370),
		            new Markup(MarkupType.Code, 372, 379),
		            new Markup(MarkupType.Code, 443, 448),
		            new Markup(MarkupType.Code, 450, 456),
		            new Markup(MarkupType.Code, 458, 463),
		            new Markup(MarkupType.Bold, 0, 9),
		            new Markup(MarkupType.Bold, 67, 79),
		            new Markup(MarkupType.Bold, 84, 92),
		            new Markup(MarkupType.Bold, 254, 269),
		            new Markup(MarkupType.Italic, 14, 20)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Sequences don’t hold a reference to the items of the collection. They’re created based on the iterator of the original collection and keep a reference to all the intermediate operations that need to be performed."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Unlike transformations on collections, intermediate transformations on sequences are not inline functions — inline functions cannot be stored and sequences need to store them. Looking at how an intermediate operation like map is implemented, we can see that the transform function is kept in a new instance of a Sequence:",
		        L(
		            new Markup(MarkupType.Code, 222, 225),
		            new Markup(MarkupType.Code, 312, 320),
		            new Markup(
		                MarkupType.Link,
		                222,
		                225,
		                "https://github.com/JetBrains/kotlin/blob/master/libraries/stdlib/common/src/generated/_Sequences.kt#L860"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "public fun <T, R> Sequence<T>.map(transform: (T) -> R): Sequence<R>{      \n   return TransformingSequence(this, transform)\n}",
		        L(new Markup(MarkupType.Bold, 85, 105))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "A terminal operation, like first, iterates through the elements of the sequence until the predicate condition is matched.",
		        L(
		            new Markup(MarkupType.Code, 27, 32),
		            new Markup(
		                MarkupType.Link,
		                27,
		                32,
		                "https://github.com/JetBrains/kotlin/blob/master/libraries/stdlib/common/src/generated/_Sequences.kt#L117"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "public inline fun <T> Sequence<T>.first(predicate: (T) -> Boolean): T {\n   for (element in this) if (predicate(element)) return element\n   throw NoSuchElementException(“Sequence contains no element matching the predicate.”)\n}"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "If we look at how a sequence like TransformingSequence (used in the map above) is implemented, we’ll see that when next is called on the sequence iterator, the transformation stored is also applied.",
		        L(
		            new Markup(MarkupType.Code, 34, 54),
		            new Markup(MarkupType.Code, 68, 71)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "internal class TransformingIndexedSequence<T, R> \nconstructor(private val sequence: Sequence<T>, private val transformer: (Int, T) -> R) : Sequence<R> {",
		        L(
		            new Markup(
		                MarkupType.Bold,
		                109,
		                120
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.CodeBlock,
		        "override fun iterator(): Iterator<R> = object : Iterator<R> {\n   …\n   override fun next(): R {\n     return transformer(checkIndexOverflow(index++), iterator.next())\n   }\n   …\n}",
		        L(
		            new Markup(MarkupType.Bold, 83, 89),
		            new Markup(MarkupType.Bold, 107, 118)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Independent on whether you’re using collections or sequences, the Kotlin Standard Library offers quite a wide range of operations for both, like find, filter, groupBy and others. Make sure you check them out, before implementing your own version of these.",
		        L(
		            new Markup(MarkupType.Code, 145, 149),
		            new Markup(MarkupType.Code, 151, 157),
		            new Markup(MarkupType.Code, 159, 166),
		            new Markup(
		                MarkupType.Link,
		                193,
		                207,
		                "https://kotlinlang.org/api/latest/jvm/stdlib/kotlin.collections/#functions"
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Collections and sequences"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Let’s say that we have a list of objects of different shapes. We want to make the shapes yellow and then take the first square shape."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Let’s see how and when each operation is applied for collections and when for sequences"
		    ),
		    new Paragraph(
		        ParagraphType.Subhead,
		        "Collections"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "map is called — a new ArrayList is created. We iterate through all items of the initial collection, transform it by copying the original object and changing the color, then add it to the new list.",
		        L(new Markup(MarkupType.Code, 0, 3))
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "first is called — we iterate through each item until the first square is found",
		        L(new Markup(MarkupType.Code, 0, 5))
		    ),
		    new Paragraph(
		        ParagraphType.Subhead,
		        "Sequences"
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "asSequence — a sequence is created based on the Iterator of the original collection",
		        L(new Markup(MarkupType.Code, 0, 10))
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "map is called — the transformation is added to the list of operations needed to be performed by the sequence but the operation is NOT performed",
		        L(
		            new Markup(MarkupType.Code, 0, 3),
		            new Markup(MarkupType.Bold, 130, 133)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "first is called — this is a terminal operation, so, all the intermediate operations are triggered, on each element of the collection. We iterate through the initial collection applying map and then first on each of them. Since the condition from first is satisfied by the 2nd element, then we no longer apply the map on the rest of the collection.",
		        L(new Markup(MarkupType.Code, 0, 5))
		    ),

		    new Paragraph(
		        ParagraphType.Text,
		        "When working with sequences no intermediate collection is created and since items are evaluated one by one, map is only performed on some of the inputs."
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Performance"
		    ),
		    new Paragraph(
		        ParagraphType.Subhead,
		        "Order of transformations"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Independent of whether you’re using collections or sequences, the order of transformations matters. In the example above, first doesn’t need to happen after map since it’s not a consequence of the map transformation. If we reverse the order of our business logic and call first on the collection and then transform the result, then we only create one new object — the yellow square. When using sequences — we avoid creating 2 new objects, when using collections, we avoid creating an entire new list.",
		        L(
		            new Markup(MarkupType.Code, 122, 127),
		            new Markup(MarkupType.Code, 157, 160),
		            new Markup(MarkupType.Code, 197, 200)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Because terminal operations can finish processing early, and intermediate operations are evaluated lazily, sequences can, in some cases, help you avoid doing unnecessary work compared to collections. Make sure you always check the order of the transformations and the dependencies between them!"
		    ),
		    new Paragraph(
		        ParagraphType.Subhead,
		        "Inlining and large data sets consequences"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Collection operations use inline functions, so the bytecode of the operation, together with the bytecode of the lambda passed to it will be inlined. Sequences don’t use inline functions, therefore, new Function objects are created for each operation.",
		        L(
		            new Markup(
		                MarkupType.Code,
		                202,
		                210
		            )
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "On the other hand, collections create a new list for every transformation while sequences just keep a reference to the transformation function."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "When working with small collections, with 1–2 operators, these differences don’t have big implications so working with collections should be ok. But, when working with large lists the intermediate collection creation can become expensive; in such cases, use sequences.",
		        L(
		            new Markup(MarkupType.Bold, 18, 35),
		            new Markup(MarkupType.Bold, 119, 130),
		            new Markup(MarkupType.Bold, 168, 179),
		            new Markup(MarkupType.Bold, 258, 267)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Unfortunately, I’m not aware of any benchmarking study done that would help us get a better understanding on how the performance of collections vs sequences is affected with different sizes of collections or operation chains."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Collections eagerly evaluate your data while sequences do so lazily. Depending on the size of your data, pick the one that fits best: collections — for small lists or sequences — for larger ones, and pay attention to the order of the transformations."
		    )
		);

		public static readonly IReadOnlyList<Paragraph> paragraphsPost6 = L(
		    new Paragraph(
		        ParagraphType.Text,
		        "The Android Studio logo redesign caught the attention of the developer community since its sneak peek at the Android Developer Summit. We are thrilled to release the new Android Studio logo with the stable release of Flamingo. Now that the new logo is available to most Android Studio users, we can examine the design changes in greater detail and decode their meaning."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "This case study offers a comprehensive overview of the design journey, from identifying the initial problem to the final outcome. It explores the critical brand elements that the team needed to consider and the tools used throughout the redesign process. This case study also delves into the various stages of design exploration, highlighting the efforts to create a modern logo while honoring the Android Studio brand's legacy."
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Identifying the problem"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "You told us the Android Studio logo looked a little weird and complicated. It doesn't shrink down well and it's way too similar to the emulator. We heard you!"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "The Android Studio logo used between 2020 and 2022 was well-suited for print, but it posed challenges when used as an application icon. Its readability suffered when reduced to smaller sizes, and its similarity to the emulator caused confusion."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Additionally, the use of color alone to differentiate between Canary and Stable versions made it difficult for users with color vision deficiencies."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "The redesign aimed to resolve these concerns by creating a logo that was easy to read, visually distinctive, and followed the OS guidelines when necessary, ensuring accessibility. The new design also maintained a connection with the Android logo family while honoring its legacy."
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "In this case study, we will delve into the version history and evolution of the Android Studio logo and how it has changed over the years."
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "A brief history of the Android Studio logo"
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "2013: The original Android Studio logo was a 3D robot that highlighted the gears and interworking of the bugdroid. At this time, the Android Emulator was the bugdroid.",
		        L(
		            new Markup(MarkupType.Bold, 0, 5)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "2014: The Android Emulator merged to a flat mark but remained otherwise unchanged.",
		        L(
		            new Markup(MarkupType.Bold, 0, 5)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "2014-2019: An updated Android Studio logo was introduced featuring an \"A\" compass in front of a green circle.",
		        L(
		            new Markup(MarkupType.Bold, 0, 10)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "2019: In Canary 3.6, the color palette was updated to match Android 10.",
		        L(
		            new Markup(MarkupType.Bold, 0, 5)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "2020-2022: With the release of Android Studio 4.1 Canary, the \"A\" compass was reduced to an abstract form placed in front of a blueprint. The Android head was also added, peeking over the top.",
		        L(
		            new Markup(MarkupType.Bold, 0, 10)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Understanding the Android brand elements"
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "When redesigning a logo, it's important to consider brand elements that unify products within an ecosystem. For the Android Developer ecosystem, the \"robot head\" is a key brand element, alongside the primaryAndroid green color. The secondary colors blue and navy, and tertiary colors like orange, can also be utilized for support."
		    ),
		    new Paragraph(
		        ParagraphType.Header,
		        "Key objectives"
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Iconography: use recognizable and appropriate symbols, such as compass \"A\" for Android Studio or a device for Android Emulator, to convey the purpose and functionality clearly and quickly.",
		        L(
		            new Markup(MarkupType.Bold, 0, 12)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Enhance recognition and scalability: the Android Studio and Android Emulator should prioritize legibility and scalability, ensuring that they can be easily recognized and understood even at smaller sizes.",
		        L(
		            new Markup(MarkupType.Bold, 0, 36)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Establish distinction: the Android Studio and Android Emulator need to be easily distinguishable, to avoid confusion.",
		        L(
		            new Markup(MarkupType.Bold, 0, 22)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Maintain brand consistency: the Android Studio and Android Emulator designs should be consistent with the overall branding and visual identity of the Android family, while still being distinctive.",
		        L(
		            new Markup(MarkupType.Bold, 0, 27)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Ensure accessibility: the logo should be accessible to all users, including those with visual impairments. This means using clear shapes, colors, and contrast.",
		        L(
		            new Markup(MarkupType.Bold, 0, 21)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Follow OS guidelines: the updated application icon must align with the Android visual language and conform to the guidelines of macOS, Windows, and Linux operating systems, ensuring consistency and coherence across all platforms.",
		        L(
		            new Markup(MarkupType.Bold, 0, 21)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Bullet,
		        "Ensure versatility: the Android Studio logo should be versatile enough to work in a variety of sizes and contexts, such as on different devices and platforms.",
		        L(
		            new Markup(MarkupType.Bold, 0, 20)
		        )
		    ),
		    new Paragraph(
		        ParagraphType.Text,
		        "Read More",
		        L(
		            new Markup(
		                MarkupType.Link,
		                0,
		                9,
		                "https://android-developers.googleblog.com/2023/05/redesigning-android-studio-logo.html"
		            )
		        )
		    )
		);

		public static readonly Post post1 = new Post(
		    Id: "dc523f0ed25c",
		    Title: "A Little Thing about Android Module Paths",
		    Subtitle: "How to configure your module paths, instead of using Gradle’s default.",
		    Url: "https://medium.com/androiddevelopers/gradle-path-configuration-dc523f0ed25c",
		    Publication: ThePublication,
		    Metadata: new Metadata(
		        Author: pietro,
		        Date: "August 02",
		        ReadTimeMinutes: 1
		    ),
		    Paragraphs: paragraphsPost1,
		    ImageId: "post_1",
		    ImageThumbId: "post_1_thumb"
		);

		public static readonly Post post2 = new Post(
		    Id: "7446d8dfd7dc",
		    Title: "Dagger in Kotlin: Gotchas and Optimizations",
		    Subtitle: "Use Dagger in Kotlin! This article includes best practices to optimize your build time and gotchas you might encounter.",
		    Url: "https://medium.com/androiddevelopers/dagger-in-kotlin-gotchas-and-optimizations-7446d8dfd7dc",
		    Publication: ThePublication,
		    Metadata: new Metadata(
		        Author: manuel,
		        Date: "July 30",
		        ReadTimeMinutes: 3
		    ),
		    Paragraphs: paragraphsPost2,
		    ImageId: "post_2",
		    ImageThumbId: "post_2_thumb"
		);

		public static readonly Post post3 = new Post(
		    Id: "ac552dcc1741",
		    Title: "From Java Programming Language to Kotlin — the idiomatic way",
		    Subtitle: "Learn how to get started converting Java Programming Language code to Kotlin, making it more idiomatic and avoid common pitfalls, by…",
		    Url: "https://medium.com/androiddevelopers/from-java-programming-language-to-kotlin-the-idiomatic-way-ac552dcc1741",
		    Publication: ThePublication,
		    Metadata: new Metadata(
		        Author: florina,
		        Date: "July 09",
		        ReadTimeMinutes: 1
		    ),
		    Paragraphs: paragraphsPost3,
		    ImageId: "post_3",
		    ImageThumbId: "post_3_thumb"
		);

		public static readonly Post post4 = new Post(
		    Id: "84eb677660d9",
		    Title: "Locale changes and the AndroidViewModel antipattern",
		    Subtitle: "TL;DR: Expose resource IDs from ViewModels to avoid showing obsolete data.",
		    Url: "https://medium.com/androiddevelopers/locale-changes-and-the-androidviewmodel-antipattern-84eb677660d9",
		    Publication: ThePublication,
		    Metadata: new Metadata(
		        Author: jose,
		        Date: "April 02",
		        ReadTimeMinutes: 1
		    ),
		    Paragraphs: paragraphsPost4,
		    ImageId: "post_4",
		    ImageThumbId: "post_4_thumb"
		);

		public static readonly Post post5 = new Post(
		    Id: "55db18283aca",
		    Title: "Collections and sequences in Kotlin",
		    Subtitle: "Working with collections is a common task and the Kotlin Standard Library offers many great utility functions. It also offers two ways of…",
		    Url: "https://medium.com/androiddevelopers/collections-and-sequences-in-kotlin-55db18283aca",
		    Publication: ThePublication,
		    Metadata: new Metadata(
		        Author: florina,
		        Date: "July 24",
		        ReadTimeMinutes: 4
		    ),
		    Paragraphs: paragraphsPost5,
		    ImageId: "post_5",
		    ImageThumbId: "post_5_thumb"
		);

		public static readonly Post post6 = new Post(
		    Id: "55db18283ac0",
		    Title: "Redesigning the Android Studio Logo",
		    Subtitle: "A case study offering a comprehensive overview of the design journey of the Android Studio product logo.",
		    Url: "https://android-developers.googleblog.com/2023/05/redesigning-android-studio-logo.html",
		    Publication: ThePublication,
		    Metadata: new Metadata(
		        Author: androidstudioteam,
		        Date: "May 10",
		        ReadTimeMinutes: 5
		    ),
		    Paragraphs: paragraphsPost6,
		    ImageId: "post_6",
		    ImageThumbId: "post_6_thumb"
		);

		public static readonly PostsFeed Posts = new PostsFeed(
		        HighlightedPost: post6,
		        RecommendedPosts: L(post1, post2, post3),
		        PopularPosts: L(
		            post5,
		            post1 with { Id = "post6" },
		            post2 with { Id = "post7" }
		        ),
		        RecentPosts: L(
		            post6,
		            post3 with { Id = "post8" },
		            post4 with { Id = "post9" },
		            post5 with { Id = "post10" }
		        )
		    );
	}

}
