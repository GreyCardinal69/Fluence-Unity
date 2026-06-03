 
---

# Fluence Standard Library Reference

This document details the built-in functions, types, and libraries available in Fluence.

## Table of Contents
- [Global Functions](#global-functions)
- [Type Conversion](#type-conversion)
- [String Methods](#string-methods)
- [List Methods](#list-methods)
- [Math Library](#math-library)
- [IO Library (File & Directory)](#io-library)
- [Collections (Map, Set, Stack)](#collections)
- [Time & Stopwatch](#time--stopwatch)

---

## Global Functions
These functions are available in any scope without importing a library.

| Function | Description |
| :--- | :--- |
| `print(value)` | Prints a value to the standard output (no newline). |
| `printl(value)` | Prints a value to the standard output followed by a newline. |
| `input()` | Reads a line of text from the standard input. |
| `clear()` | Clears the console output buffer. |
| `readAndClear()` | Waits for user input (Enter), then clears the console. |
| `typeof(value)` | Returns the type name of the given value (e.g., "string", "number", "List"). |
| `Random.random()` | Returns a random float between 0.0 and 1.0. |
| `Random.between_inclusive(min, max)` | Returns a random integer between `min` (inclusive) and `max` (inclusive). |
| `Random.between_exclusive(min, max)` | Returns a random integer between `min` (exclusive) and `max` (exclusive). |

---

## Type Conversion

| Function | Description |
| :--- | :--- |
| `to_int(value)` | Converts a number or string to an Integer (32-bit). |
| `to_long(value)` | Converts a number or string to a Long (64-bit). |
| `to_float(value)` | Converts a number or string to a Float (32-bit). |
| `to_double(value)` | Converts a number or string to a Double (64-bit). |
| `to_bool(value)` | Converts a value to a boolean (`0` or `"false"` becomes `false`). |
| `to_string(value)` | Converts any value to its string representation. |

---

## String Methods
Strings in Fluence are objects and have built-in methods.
*Usage:* `"Hello".upper()`

| Method | Arguments | Description |
| :--- | :--- | :--- |
| `length()` | None | Returns the number of characters in the string. |
| `upper()` | None | Returns a copy of the string in uppercase. |
| `lower()` | None | Returns a copy of the string in lowercase. |
| `trim()` | None | Removes whitespace from both ends. |
| `trim_start()` | None | Removes whitespace from the start. |
| `trim_end()` | None | Removes whitespace from the end. |
| `contains(str)` | `str` | Returns `true` if the string contains the substring. |
| `starts_with(str)`| `str` | Returns `true` if the string starts with the substring. |
| `ends_with(str)` | `str` | Returns `true` if the string ends with the substring. |
| `index_of(char)` | `char` | Returns the index of the first occurrence of `char`. |
| `last_index_of(char)`| `char` | Returns the index of the last occurrence of `char`. |
| `replace(old, new)` | `string`, `string` | Replaces all occurrences of `old` with `new`. |
| `sub(start)` | `int` | Returns a substring starting from index `start`. |
| `sub(start, len)` | `int`, `int` | Returns a substring of length `len` starting from `start`. |
| `insert(idx, str)` | `int`, `string` | Inserts `str` at the specified index. |
| `pad_left(n, char)`| `int`, `char` | Pads the string on the left with `char` until length is `n`. |
| `pad_right(n, char)`| `int`, `char` | Pads the string on the right with `char` until length is `n`. |

### StringBuilder
A mutable string class for efficient concatenation.

| Method | Description |
| :--- | :--- |
| `StringBuilder()` | Creates a new empty StringBuilder. |
| `StringBuilder(capacity)` | Creates a StringBuilder with initial capacity. |
| `.append(str)` | Appends a string. |
| `.append(char, count)` | Appends a character `count` times. |
| `.append_line(str)` | Appends a string followed by a newline. |
| `.append_join(sep, list)` | Appends list elements joined by a separator. |
| `.insert(idx, str)` | Inserts a string at the given index. |
| `.clear()` | Clears the builder. |
| `.length()` | Returns the current length. |
| `.to_string()` | Returns the final constructed string. |

---

## List Methods
Lists are dynamic arrays.
*Usage:* `list = [1, 2]; list.push(3);`

| Method | Arguments | Description |
| :--- | :--- | :--- |
| `push(item)` | `item` | Adds an item to the end of the list. |
| `push_range(list)` | `list` | Adds all elements from another list to the end. |
| `insert(idx, item)` | `int`, `item` | Inserts an item at the specified index. |
| `insert_range(idx, list)` | `int`, `list` | Inserts elements from another list at index. |
| `remove(item)` | `item` | Removes the first occurrence of `item`. |
| `remove_at(idx)` | `int` | Removes the item at the specified index. |
| `remove_range(idx, count)` | `int`, `int` | Removes `count` items starting at `idx`. |
| `element_at(idx)` | `int` | Returns the item at index (same as `list[idx]`). |
| `index_of(item)` | `item` | Returns the index of the item, or -1 if not found. |
| `last_index_of(item)` | `item` | Returns the last index of the item. |
| `contains(item)` | `item` | Returns `true` if the list contains the item. |
| `reverse()` | None | Reverses the list in place. |
| `clear()` | None | Removes all elements. |
| `length()` | None | Returns the number of elements. |

---

## Math Library
Import using: `use FluenceMath;`

**Constants:** `Math.Pi`, `Math.E`, `Math.Tau`

| Function | Description |
| :--- | :--- |
| `abs(n)` | Returns the absolute value. |
| `ceil(n)` | Rounds up to the nearest integer. |
| `floor(n)` | Rounds down to the nearest integer. |
| `round(n)` | Rounds to the nearest integer. |
| `sqrt(n)` | Returns the square root. |
| `exp(n)` | Returns `e` raised to power `n`. |
| `log(n)` | Returns the natural logarithm (base `e`). |
| `log10(n)` | Returns the base-10 logarithm. |
| `log2(n)` | Returns the base-2 logarithm. |
| `min(a, b)` | Returns the smaller of two numbers. |
| `max(a, b)` | Returns the larger of two numbers. |
| `clamp(n, min, max)` | Clamps `n` between `min` and `max`. |
| **Trigonometry** | `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `sinh`, `cosh`, `tanh` |
| `atan2(y, x)` | Returns the angle whose tangent is `y/x`. |
---

## IO Library
Import using: `use FluenceIO;`

### File
| Function | Description |
| :--- | :--- |
| `File.read(path)` | Returns the contents of a file as a string. |
| `File.write(path, text)` | Writes text to a file (overwrites). |
| `File.append_text(path, text)` | Appends text to the end of a file. |
| `File.create(path)` | Creates an empty file. |
| `File.delete(path)` | Deletes a file. |
| `File.exists(path)` | Returns `true` if the file exists. |
| `File.move(old, new)` | Moves or renames a file. |

### Directory (Dir)
| Function | Description |
| :--- | :--- |
| `Dir.create(path)` | Creates a directory. |
| `Dir.delete(path)` | Deletes a directory. |
| `Dir.exists(path)` | Returns `true` if the directory exists. |
| `Dir.get_files(path)` | Returns a List of file paths in the directory. |
| `Dir.get_dirs(path)` | Returns a List of subdirectory paths. |
| `Dir.get_current()` | Returns the current working directory. |
| `Dir.move(path, new)` | Moves a directory. |

### Path
| Function | Description |
| :--- | :--- |
| `Path.get_file_name(path)` | Returns "file.txt" from "c:/file.txt". |
| `Path.get_file_name_raw(path)` | Returns "file" from "c:/file.txt". |
| `Path.get_dir_name(path)` | Returns the directory part of the path. |
| `Path.get_full_path(path)` | Resolves a relative path to absolute. |
| `Path.has_extension(path)` | Returns `true` if path has an extension. |
| `Path.change_extension(p, ext)` | Changes the extension of a path. |
| `Path.exists(path)` | Returns `true` if path exists (file or dir). |
| `Path.dir_sep_char()` | Returns `/` or `\` depending on OS. |

---

## Collections
Advanced data structures available globally.

### Dictionary (Map)
A key-value store created via `{ key -> val }`.

| Method | Arguments | Description |
| :--- | :--- | :--- |
| `.add(key, val)` | `key`, `val` | Adds or updates a key-value pair. |
| `.get(key)` | `key` | Returns the value, or `nil` if missing. |
| `.get(key, default)` | `key`, `val` | Returns the value, or `default` if missing. |
| `.contains_key(key)` | `key` | Returns `true` if the key exists. |
| `.remove(key)` | `key` | Removes the key and its value. |
| `.keys()` | None | Returns a List of all keys. |
| `.values()` | None | Returns a List of all values. |
| `.count()` | None | Returns the number of items. |
| `.is_empty()` | None | Returns `true` if the map has no items. |
| `.clear()` | None | Removes all items. |
| `.to_string()` | None | Returns string representation `{ k -> v, ... }`. |

### HashSet
A collection of unique values.
| Method | Description |
| :--- | :--- |
| `HashSet()` | Creates a new Set. |
| `.add(item)` | Adds item. |
| `.remove(item)` | Removes item. |
| `.contains(item)` | Returns `true` if item is in the set. |
| `.count()` | Returns number of items. |
| `.clear()` | Empties the set. |
| `.to_string()` | Returns string representation. |

### Stack
A Last-In-First-Out (LIFO) collection.
| Method | Description |
| :--- | :--- |
| `Stack()` | Creates a new Stack. |
| `.push(item)` | Pushes item onto top. |
| `.pop()` | Removes and returns top item. |
| `.peek()` | Returns top item without removing. |
| `.count()` | Returns number of items. |
| `.empty()` | Returns `true` if stack is empty. |

---

## Time & Stopwatch

### Time
| Function | Description |
| :--- | :--- |
| `Time.now()` | Returns current Unix timestamp (ms). |
| `Time.utc_now()` | Returns current UTC time as ISO string. |
| `Time.sleep(ms)` | Pauses execution for `ms` milliseconds. |

### Stopwatch
Used for high-precision benchmarking.
| Method | Description |
| :--- | :--- |
| `Stopwatch()` | Creates a new Stopwatch. |
| `.start()` | Starts measuring time. |
| `.stop()` | Stops measuring. |
| `.reset()` | Stops and resets to 0. |
| `.restart()` | Resets and starts immediately. |
| `.elapsed_ms()` | Returns elapsed time in milliseconds. |
| `.elapsed_ticks()` | Returns elapsed timer ticks. |

--

