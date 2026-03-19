Garbage collection in Unity (how to avoid it)

Garbage collection in Web GL
Garbage collection in Web GL works a little differently to other platforms.

Normally the garbage collector runs as soon as it’s needed, which may mean that a collection cycle is performed in the middle of a frame to make space for a new data allocation.

However, when building a game for Web GL, garbage collection can only take place on the next frame.

What this means is that, unlike on other platforms, if you create an excessive amount of garbage in a single frame in a Web GL project, the garbage collector won’t be able to do anything about it until the next frame, potentially causing the code to fail.

So how can you reduce the impact of garbage collection in your game?

Garbage collection spikes happen because the garbage collector performs a ‘stop-the-world’ collection cycle, during which Unity’s main thread is paused and unable to do anything else.

However, since Unity 2019, Unity’s garbage collector has supported Incremental Garbage Collection, which helps to alleviate the spikes that excessive garbage collection can cause.

Incremental Garbage Collection in Unity
While incremental garbage collection doesn’t reduce the work that’s required to perform a garbage collection cycle it can split it up into discrete phases that take place over a number of frames instead of all at once.

The benefit of this is that the noticeable impact of the garbage collection process is usually lower, since the processing time is spread out, avoiding a single spike.

Unity does this by balancing the evaluation phase of the garbage collection process over several steps, typically when the CPU has some spare time, such as when it’s waiting for the next frame, as it may do when V-Sync is enabled or a target frame rate is being used.

However, while using incremental garbage collection can reduce the impact of collection spikes, it also introduces an additional processing overhead.

This happens because, since the game is not stopped during the evaluation phase, data references may change while the collection process is taking place.

To fix this, additional write barriers are used to make sure that the garbage collector knows if a reference has changed, allowing it to rescan the data, at the expense of additional processing.

And, if enough references change during the collection process, so much so that it’s unable to finish, the garbage collector will fall back to non-incremental garbage collection instead.

Incremental garbage collection is enabled by default in Unity 2019 or higher, however, it is possible to disable it.

Screenshot of the incremental garbage collection setting in the Unity Player menu
Incremental Garbage Collection, which is enabled by default, splits the evaluation stage of the garbage collection process into multiple steps, reducing its impact.

Typically you might choose to do this if, in your project, a lot of references are expected to frequently change, in which case the incremental garbage collector is likely to have to fall back to non-incremental collection more often than not.

For more information on how incremental garbage collection works in Unity, see the official documentation here.

How to run the garbage collector manually
In many cases, letting Unity manage your game’s memory for you is probably going to be the best thing to do.

However there may be times when you want to manually trigger a garbage collection cycle, in which case, so long as the garbage collector isn’t disabled, you can call a regular, non-incremental collection by using the Collect Function.

Like this:

System.GC.Collect();
Or, if incremental garbage collection is enabled, by using the Collect Incremental Function.

Like This:

using UnityEngine;
using UnityEngine.Scripting;

public class ForceCollection : MonoBehaviour
{
    void ForceCollectGarbage()
    {
        // specifies the amount of time to spend in Garbage Collection (in nanoseconds)
        GarbageCollector.CollectIncremental(3000000);
    }
}
While it’s possible to limit the impact of garbage collection, either by using incremental garbage collection or by manually choosing when it happens, by far the simplest and easiest way to manage garbage in your game is to avoid creating it in the first place.

So what causes garbage and how can you prevent it?

How to reduce garbage in Unity
The most effective way to reduce garbage in Unity is to, as much as possible, avoid creating it.

But what creates garbage?

When is garbage created?
Generally speaking, a new allocation of memory is likely to become garbage when:

It needs to be stored on the heap, and
The allocation of memory isn’t used again.
But how can you know what goes on the heap and what goes on the stack?

Generally, local value type variables, such as integers, floats, doubles, booleans and data structs (if they’re made up of value types) are all stored on the Stack, which is fast, temporary data that doesn’t create any garbage.

So for example, when you call a function, any local values you declare and use within it will be allocated on the stack and won’t create any garbage.

Like this:

void GarbageFunction()
{
    int number = 2;
    int otherNumber = 2;

    int total = number + otherNumber;
}
This typically only applies when declaring temporary variables locally in a function, while the member variables you declare at the top of a class, for example, are stored with the class itself on the heap.

Reference types, such as new instances of classes, have their memory allocated on the heap, while a pointer to their location exists on the stack.

This means that simply creating a reference to a class that already exists doesn’t generate garbage on its own, since the stack reference to the location of the data simply links to an existing allocation of memory on the heap, it doesn’t create a new one.

For example, multiple new stack references can all point to the same allocation of heap data without creating any garbage.

Likewise, if the stack pointer doesn’t actually reference anything, because it’s empty, for example, then no memory is allocated to the heap and no garbage will be created.

However, when you create a new instance of a class, then that new memory will be allocated to the heap.

Like this:

public class GarbageCreator : MonoBehaviour
{
    void GarbageFunction()
    {
        Number number = new Number(2);
        Number otherNumber = new Number(2);

        int total = number.value + otherNumber.value;
    }
}

public class Number
{
    public int value;

    public Number(int newValue)
    {
        value = newValue;
    }
}
While this would be an unusual way to add two numbers together, it does, at least, demonstrate a basic way of creating a small amount of garbage.

In this case, each of the new Number Classes are only used temporarily, meaning that, as soon as the function is out of scope, they are no longer needed.

When this happens their heap data is no longer referenced from the stack, making it garbage.

Which isn’t necessarily a problem, especially since the amount of garbage that this function creates is quite small, at 40 bytes.

However, if you allocate new temporary memory to the heap frequently, such as during every frame in Update, even a small amount of garbage can quickly add up and impact your game’s performance.

How to find out how much garbage you’re creating
The best way to find out how much garbage your code is generating is by using Unity’s Profiler.

For example, the CPU Profiler in Heirarchy view can tell you how much garbage has been allocated in a particular frame and, with Deep Profiling enabled, can even show you which function is responsible for it.

Screenshot of 40b of Garbage in the Unity Profiler

However, while running the Profiler in the editor can show you if you’re creating any garbage at a glance, for a reliable measurement, you’ll need to run the project as a development build in the Standalone Player.

At which point, selecting Auto Connect Profiler will allow you to monitor the built game as it runs.

Auto Connect Profiler

This is useful, since there are some actions that can, reportedly, cause an amount of garbage when running in the editor, but that won’t in the Standalone Player.

Generally speaking, any temporary allocation of heap data is likely to create garbage, however, it’s not always clear which processes will create garbage and which ones won’t.

So which processes cause garbage, and how can you avoid creating too much?

Which processes cause garbage in Unity?
When writing a function in Unity, it’s generally a good idea to avoid temporarily allocating heap data.

However, unless you’re familiar with what will actually cause garbage, it can be tricky to know what’s ok and what’s not.

Particularly when there are a number of extremely common processes that you might use in a function that can create garbage without you knowing.

So, what causes garbage, and what doesn’t?

Arrays and lists create garbage
Collections, such as lists and arrays, are stored on the heap, meaning that they create garbage when they’re used temporarily.

Meaning that if you use a new array in a function, as soon as the function ends, the memory will need to be cleaned up again.

Using arrays or lists as member variables in your class isn’t a problem, and using a collection once in a function isn’t necessarily an issue either.

However, there are a number of common functions built into Unity that have an array return type that you might typically want to use every frame.

Such as Raycast All, for example:

RaycastHit[] hits;

void Update()
{
    // This will create garbage
    hits = Physics.RaycastAll(new Ray(Vector3.zero, Vector3.forward));
}
Raycast All works by returning a new array every time it’s called, which, if you call it every frame, to perform a ground check, for example, can cause a garbage problem.

As a result, Unity provides an alternative version of Raycast All, Raycast Non-Alloc, which works in the same way, but uses an existing array instead.

Like this:

RaycastHit[] hits = new RaycastHit[10];

void Update()
{
    // This won't create garbage!
    Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), hits);
}
This works because Raycast Non-Alloc is simply reusing the same array like a buffer, meaning that no new memory is being allocated.

New classes create garbage
Declaring a new, temporary, class instance, which is a reference type that’s stored on the heap, will usually create garbage.

One reason you might want to create local instances of a class is to organise new data into a specific structure.

Like this:

public class ExampleClass : MonoBehaviour
{
    void Start()
    {
        Stats stats = new Stats(10, 30, 130);
    }
}

public class Stats
{
    public int hp, mp, exp;

    public Stats(int health, int magic, int experience)
    {
        hp = health;
        mp = magic;
        exp = experience;
    }
}
However, because classes are stored on the heap, even if the class itself only contains value-type data, creating temporary instances in this way will always create garbage.

Which is why, typically, if you just want to use your class as a data container to pass values around, and you don’t need to work with it in the same way as you would a referencable object, such as to check if it’s null, you may be better off using a Struct instead.

Structs don’t (usually) create garbage
In Unity, a struct is a custom data template that can hold multiple values and functions in a single place and, in many ways, works in a similar way to a class.

Like this: 

public class ExampleStruct : MonoBehaviour
{
    void Start()
    {
        Stats stats = new Stats(10, 30, 130);
    }
}

public struct Stats
{
    public int hp, mp, exp;

    public Stats(int health, int magic, int experience)
    {
        hp = health;
        mp = magic;
        exp = experience;
    }
}
However, while a class is a reference type that’s stored on the heap, which means that the same allocation of data can be referenced multiple times, or it can be null, Structs, because they’re value types, are simply copies of known data.

How is that different?

A reference to a class is the equivalent of a person physically pointing to a piece of information written on a blackboard.

If you were to ask them what the information is they could show you and, even if the data had changed, it would be correct.

What’s more, multiple people in the class could point to the same information and, even though it’s only stored in one place, any of them could tell you what it was.

A value type, however, such as a struct, would be more like writing the information down on a piece of paper. In this case, it’s simply a copy of the original, meaning that if the original ever changed, the copy wouldn’t.

Which is how value types work compared to reference types.

However, the benefit of this is that, because structs are values, copies of data with no allocation on the heap, they don’t create any garbage.

Usually…

It’s still possible to create a struct that generates garbage if the struct contains new data allocations that, themselves, need to be stored on the heap, such as initialising an array inside a struct, for example.

However, just including a reference variable inside of your struct isn’t necessarily going to generate any garbage.

After all, a variable that points to a data allocation on the heap is, itself, just a value that’s stored on the stack.

Meaning that, until you allocate new data on the heap, by creating a new class or initialising an array, for example, you won’t need to worry about creating any garbage.

And, even then, it’ll only become garbage if it’s actually stored on the heap.

There are many data types in Unity that often need to be newly created when they’re used, such as vector 3 values, colours or rays, for example.

However, because they’re not classes, they’re structs, they don’t create any garbage.

Simply avoiding allocating new heap data temporarily can be a simple way to avoid garbage.

However, there are a number of, seemingly, simple processes in Unity that can create garbage unexpectedly, because of how they work behind the scenes.

Coroutines create garbage, but most of it can be avoided
Coroutines do create a small amount of garbage, both when they’re first started and, typically, whenever they’re suspended for a period of time, such as when delaying the coroutine by a number of seconds.

Like this:

IEnumerator WaitFiveSeconds()
{ 
    yield return new WaitForSeconds(5);
    // I waited five seconds!
}
This happens because Wait For Seconds is not a function, it’s a class, meaning that, by calling a new wait for seconds instance, you’re actually creating a new class, which creates garbage.

However, this can be avoided by caching an instance of the wait for seconds class with a preconfigured delay and using that instead.

Like this:

WaitForSeconds delay = new WaitForSeconds(5);

IEnumerator WaitFiveSeconds()
{ 
    yield return delay;
    // I waited five seconds!
}
This works by creating a new instance of the wait for seconds class as a member variable, meaning that it’s no longer a temporary allocation and it will be stored with the script.

This can be useful if you want to use the same wait for seconds delay over and over again, without allocating any extra memory or creating any garbage.

However, you may decide that it’s actually better to use a new instance of the class and allow the yield instruction to create garbage instead.

For example, if you’re only using the delay once, but the script it’s declared in will be used beyond the life of the coroutine, keeping it around in memory doesn’t really make a lot of sense.

In which case, so long as you’re only using it once, declaring it as a new wait for seconds instance inside the coroutine, and allowing Unity to clear it up when it no longer needs it, might be more useful to you.

However, the benefits of doing it this way are likely to be quite small.

Instead, as a general approach, it’s usually better to avoid creating garbage whenever you can and, as much as possible, allocate all of the memory you’re going to need for a scene upfront instead.

Instantiating objects in Unity creates garbage
Creating and destroying objects is a commonly used example of how new allocations of data can create unnecessary garbage in Unity.

This is because the Instantiate Function, which is used to spawn new objects in a scene when it runs, creates an amount of garbage every time it’s called.

In the same way that it’s generally a good approach to reuse data allocations wherever possible, reusing old objects, instead of creating new ones, is usually the best way to avoid creating garbage when calling the instantiate function.

This is a process known as Object Pooling and, while it can be set up in many different ways, typically involves keeping a number of reusable objects ready in the scene, and turning them on and off as they’re needed.

Combining strings creates garbage
Strings are an unusual type of data.

While you might consider a string to be a value type, they are in fact immutable reference types.

An Immutable Type is, basically, read-only, which means that you can’t change the value of an immutable type without creating a new instance of it.

Meaning that strings act like reference types, in that they are stored on the heap but, when they’re accessed, they’re copied like values.

Causing garbage.

However, this tends to only happen when you try to change a string, by performing String Concatenation, for example, which is the process of adding two, or more, strings together.

Like this:

public string health;
public int hp=100;

void Update()
{
    // This causes garbage
    health = "Health: " + hp;
}
While this only creates a small amount of garbage, it’s likely that you might use string concatenation in this way to update a UI display.

Which, if you do it every frame, can cause performance problems.

So how can you avoid it?

While there are strategies for reducing garbage created by constant or excessive string concatenation, such as caching or looking up the different values you might need to display instead of adding them together, generally, the easiest way to optimise the amount of garbage that’s generated is by limiting how many times you do it.

For example, if you’re using string concatenation to show a value and a label, such as a score, or a health display, it makes sense to only update it when it changes.

Like this:

public string healthDisplay;
int healthValue = 100;
public int HealthValue
{
    get
    {
        return healthValue;
    }
    set
    {
        if (healthValue != value)
        {
            healthValue = value;
            UpdateHealth();
        }
    }
}

void UpdateHealth()
{
    healthDisplay = "Health: " + HealthValue;
}

private void Update()
{
    if (Input.GetKeyDown(KeyCode.Space))
    {
        HealthValue = Random.Range(0, 100);
    }
}
However, you can always expect to create a small amount of garbage when concatenating strings and, as a result, it’s generally a good idea to simply try not to do it too frequently or excessively, such as every frame in Update.

Luckily, the amount of garbage that’s generated when simply adding two strings together is small.

But…

There is one extremely common string-based function that you’re likely to use excessively without even thinking about it and, what’s worse, it creates a significant amount of garbage.

Debug Log creates large amounts of garbage
While string concatenation, creating new classes and initialising arrays can create tens of bytes of garbage, Debug Log, surprisingly, can create more than a thousand bytes of garbage from just a single call.

For example, this seemingly innocent debug message alone is responsible for 1.4kb of garbage:

Debug.Log("Hello");
Debug Log garbage example in Unity
Debug Log creates an alarmingly large amount of garbage.

But, surely this isn’t a problem?

After all, while you’re probably going to need to use debug messages while you’re building your game in the editor, you might not need them when your game is finished.

However…

Debug log messages, unless you remove them manually, or unless you disable the Logger, will still create relatively huge amounts of garbage in your finished project.

Which is why it’s important to manage how many log messages you’re creating and, ideally, turn them off before you build your game.

Unity garbage collection best practices
Generally speaking, as a beginner, the best way to manage garbage in Unity is by finding a balance between optimisation and convenience that’s right for your game, the performance of the platform you’re targeting and the specific function you’re writing.

For example, a small amount of garbage on a moderately powerful platform won’t have the same impact as it would in an Android game on an underpowered device.

Likewise, creating garbage once in a function isn’t necessarily a problem, unless you start to do it every frame.

So what strategy should you use to manage garbage in your Unity project?

Should you avoid all garbage?

Or is it even worth worrying about at all?

While it is usually better to create as little garbage as possible, it’s not always possible, or convenient to avoid it completely and over-optimising processes that could create garbage, unless you have a specific need to do so, can sometimes be less effective than simply being careful about when and how often you perform them.

For example, if you’re performing constant ground checks, it’s definitely going to be a good idea to use Raycast Non-Alloc to avoid allocating garbage every frame.

However, going to extreme lengths to avoid garbage when adding two strings together is generally going to be less efficient than simply doing it less often.

While forgetting to disable unnecessary debug messages, because of how much garbage they create, can quickly undo your other garbage prevention efforts.

Generally speaking, the most effective strategy to avoid creating performance problems in your game is to try to allocate the memory you’re going to need for a particular scene upfront, reuse it as much as possible, and pay close attention to any amount of garbage that’s being created frequently or excessively by using the Profiler.

And let the Garbage Collector clean up the rest.

Techniques for avoiding garbage collector spikes with managed memory
In this section, we’ll cover a few common approaches to reducing costly garbage collector spikes.

Avoid unnecessary allocations
The most basic principle of optimizing for the garbage collector is to simply avoid allocations when they aren’t necessary. There are many ways to allocate memory unintentionally when working in Unity, so it’s important to always use the profiler to confirm that you aren’t allocating memory when it isn’t needed. Here are a few key tips to reduce allocations:

Favor structs over classes
Consider using structs instead of classes for temporary objects. When classes are more appropriate or you need arrays, consider caching the object and reusing it each frame.

Be careful with string manipulation
String manipulation is a common source of unintended allocations. Strings are immutable in C#, so every time you concatenate or otherwise modify a string, a new string is allocated. Consider using StringBuilder to reduce the number of allocations when working with strings.

// This allocates 5 strings every time it is called
public string Vector2ToString(Vector2 vector)
{
    return "x: " + vector.x + " y: " + vector.y;
}

// This reuses a StringBuilder instance and only allocates
// one new string
private StringBuilder builder = new StringBuilder();
public string Vector2ToString(Vector2 vector)
{
	builder.Clear();
	builder.Append("x: ")
		.Append(vector.x)
		.Append(" y: ")
		.Append(vector.y);
	
    return builder.ToString();
}
Avoid creating APIs which return new arrays/collections
Have your APIs take a target collection as a parameter instead.

// Allocates a new array every time it is called
public float[] GetRandomValues()
{
	float[] values = new float[10];
	for (int i = 0; i < values.Length; ++i)
	{
		values[i] = Random.value;
	}
	return values;
}

// Allow the caller to decide to allocate a new array or reuse an existing one
public void GetRandomValues(float[] values)
{
	if (values == null) return;
	for (int i = 0; i < values.Length; ++i)
	{
		values[i] = Random.value;
	}
}
Avoid boxing structs
Don’t assign a struct instance to a reference or parameter of type object. When building APIs which take interface parameters that may be struct instances, consider using generics with the interface as a generic constraint rather than as the parameter type.

public interface IFoo { }
public class Foo : IFoo { }
public struct Bar : IFoo { }

public void BoxedInterface(IFoo foo)
{
	...
}

public void GenericInterface<T>(T foo) where T : IFoo
{
	...
}

public void Update()
{
	Foo foo = new Foo();
	Bar bar = new Bar();
	
    // These don't allocate
	BoxedInterface(foo);
	GenericInterface<Foo>(foo);
	GenericInterface<Bar>(bar);

	// This allocates because bar is "boxed"
	BoxedInterface(bar);
}
Be cautious when using closures
Value types outside of an anonymous function are boxed in a special class when passed to anonymous functions.

float number = 0f;

// This allocates a special reference type to wrap the "number" variable
Action addRandomValueDelegate = () =>
{
	number += Random.value;
};
Object pooling
Reusing allocated objects is one of the most common techniques to avoid garbage collection issues. When many instances of an object are needed, it’s helpful to preallocate a large collection of these objects, then remove them from the pool when they’re needed. Once the object is no longer needed, it can be returned to the pool for later use somewhere else. This technique is so common that Unity introduced their own set of generic object pool solutions in Unity 2021.

Whether you choose to use Unity’s solution or implement your own, you’ll almost certainly encounter a problem in Unity that is best solved by object pooling.

Object pooling is safe and broadly applicable. It is especially useful in Unity for GameObjects since creating and destroying GameObjects and Components has additional overhead beyond the memory allocation. Keep in mind though that object pooling does not completely avoid the garbage collector. Instead, it allows for some indirect control over when the objects in the pool are cleaned up. The garbage collector will still be responsible for cleaning the objects in the pool when the pool itself is released, but you can choose to do so at convenient times such as during a scene transition, or they can keep the pool alive for the entire lifetime of the app.

Manually timing collections
Although garbage collection is intended as an automatic process, it is possible to exert some control using the UnityEngine.Scripting.GarbageCollector API. It’s possible to completely disable the garbage collector this way, or to configure it so it only collects when manually invoked. Since no managed heap memory will be freed while automatic collection is disabled, new allocations will continually increase the size of the managed heap until either the garbage collector is reenabled or the application runs out of memory and is terminated. For this reason, disabling the garbage collector should be considered an unsafe practice, but it can make sense to do so temporarily during performance-critical parts of the game. Again, it is critical to use the profiler to confirm that the memory allocated during those phases will not dangerously inflate the heap.

Techniques for allocating unmanaged memory
The techniques for working with the garbage collector discussed so far are likely sufficient in the vast majority of cases. However, if you absolutely need to dynamically allocate memory without the cost of the garbage collector, we’ll cover a few of those options now.

Native collections
If you’ve used Unity’s C# Jobs system, you’re already familiar with their native collection types. The NativeArray<T> is a convenient way to allocate unmanaged heap memory. Unmanaged heap allocations are completely ignored by the garbage collector, which means the responsibility of deallocating the memory is returned to the developer. NativeArray<T> itself is a struct which holds a pointer to the heap memory, so copies of a NativeArray instance will point to the same data. NativeArray<T> implements the IDisposable interface, so you must either call Dispose() directly or via a using statement in order to deallocate the memory. The heap memory will be leaked if the last copy of the NativeArray<T> goes out of scope before it is disposed.

Native collections can only be used with unmanaged types, so you’ll be limited to built-in value types, pointer types, and custom structs which contain only fields of other unmanaged types.

// Valid
NativeArray<Vector3> positions = new NativeArray<Vector3>(128, Allocator.Persistent);

// Invalid
NativeArray<Transform> transforms = new NativeArray<Transform>(128, Allocator.Persistent);
Unity’s implementation of native collections comes with some built-in safety checks to warn of memory leaks, but using these types is still a bit less safe than using managed arrays. If you need to squeeze out every last drop of performance in a particularly hot loop, there are unsafe variants of the native collections which omit the safety checks.

Using spans and the stackalloc keyword
Like NativeArray<T>, arrays allocated using the stackalloc keyword are limited to arrays of unmanaged types. Unlike NativeArray<T>, stack-allocated arrays do not use heap memory at all. Stack-allocated arrays can be assigned to variables of type Span<T>. If you’re not familiar with spans, think of them like a pointer with a length. Spans have been growing in popularity and much of the .NET API has been updated to work with them. The same is unfortunately not true for the Unity API, but they can still be quite useful in your own code.

Since stack-allocated arrays are extremely fast to allocate and do not require you to explicitly deallocate them, they are most useful for small scratch buffers needed to perform some calculation inside a function. Keeping the data on the stack also benefits data locality. If you’ve been following Unity’s work on their Data Oriented Tech Stack, you’ve seen examples of how much data locality can affect performance.

Significant caution is required when performing dynamic allocations on the stack. Stack size is limited, so allocating too much on the stack can cause a StackOverflowException and terminate your app. Be particularly careful to validate the size of the allocation when it is passed as a parameter to a function. Avoid using stackalloc inside loops. These types of allocations are deallocated when the function returns, not when they go out of scope. That means a 128 byte allocation in a loop that runs 1000 times actually allocates 128 kilobytes. Instead, allocate the buffer outside the loop and reuse it for each iteration.

public string IncrementCharacters(string input)
{
		if (string.IsNullOrEmpty(input)) return input;

	// If the input string is short we will allocate the buffer directly on the stack, otherwise we'll use a
	// managed char array.
	Span<char> buffer = input.Length < 64 ? stackalloc char[input.Length] : new char[input.Length];

	for (int i = 0; i < input.Length; ++i)
	{
		buffer[i] = (char)(input[i] + 1);
	}

	// Note that we cannot return `buffer` here directly. If it is allocated on the stack, it will be deallocated
	// as soon as this function returns.
	return new string(buffer);
}
You can use stackalloc even in versions of Unity where Span<T> is not available, but you’ll need to use raw pointers inside an unsafe scope. Be careful though because pointers introduce many new ways to break your app. For instance, while spans will do bounds-checking on your index since it has a length value, pointers will not protect you. In general, reserve this type of optimization for only the most extreme cases.

unsafe
{
	char* bufferPtr = stackalloc char[64];
    
for (int i = 0; i < 64; ++i)
	{
		// No bounds checking here, so be careful!
		bufferPtr[i] = (char)(input[i] + 1);
	}

	return new string(bufferPtr);
}
Fixed size buffers
The last allocation strategy we’ll cover is fixed size buffers, which are C-style arrays of unmanaged types that are declared as fields of a struct. Fixed buffers are allocated as part of the struct itself, whereas a managed array field would be a reference to an array allocated on the heap. As the fixed name implies, the size of these buffers must be a compile-time constant. Note the slightly different syntax with the [] after the field name rather than the type.

// This struct is 64 bytes because data is stored directly in the struct
public unsafe struct FixedBuffer
{
	public fixed byte data[64];
}

// This struct is 8 bytes (depending on platform) because only a reference to the data is stored in the struct
public struct ManagedBuffer
{
	public byte[] data;
}
Because the data is stored inline as part of the struct, the semantics are a bit different than the options we’ve seen so far. A copy of a struct instance with a fixed size buffer will create a full copy of the data rather than pointing to the same data.

Key takeaways for avoiding garbage collector spikes in Unity
While memory management in C# is automatic, there are many ways to cause allocation-related slowdowns that will impact the performance of your Unity game. In this post, we’ve covered several tips that you can use today to reduce garbage collector spikes. When working with managed memory, you can avoid unnecessary allocations, leverage object pooling, and manually time collections. We also touched on a few advanced ways to better allocate unmanaged memory, such as native collections, spans and the stackalloc keyword, and fixed size buffers.

These tips will help you achieve better performance when building your Unity games. Another challenge is maintaining good user experiences at scale once you go from the profiler into production. Embrace allows Unity developers to create amazing mobile experiences with 100% unsampled data across every user journey. Get started for free today.

