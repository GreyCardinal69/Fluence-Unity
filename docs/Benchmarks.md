
# Benchmarks

This document contains performance metrics for the Fluence programming language (v0.1.3).

**Environment:**
*   **OS:** Windows 11 (10.0.26100)
*   **Runtime:** .NET 8.0.20 (X64 RyuJIT AVX2)
*   **Measurement Tool:** BenchmarkDotNet

---

## 1. Logic & Arithmetic Throughput
**Test:** Calculate `a + b` inside a loop iterating 1,000,000,000 (1 Billion) times. This tests the raw instruction dispatch speed and integer arithmetic optimization.

### Code
```rust
func Main() => {
    1_000_000_000 times {
        a = 5;
        b = 5; 
        c = a + b;
    }
}
```

### Results
| Time | Mean (us) | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **14.03 s** | 14,031,794.0 | 488,439.22 | 1,440,174.17 | - | 41.57 KB |

---

## 2. Recursion Overhead
**Test:** Recursive Fibonacci sequence calculation for N=30. This tests the overhead of function calls, stack frame creation, and return logic.

### Code
```rust
func fib(n) =>
    match n {
        n < 0 -> 0;
        1 -> 1;
        rest -> fib(n-1) + fib(n-2);
    };

func Main() => fib(30);
```

### Results
| Time | Mean (us) | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **302.24 ms** | 302,240.0 | 6,243.56 | 18,311.28 | - | 63.08 KB |

---

## 3. Complex Simulation (Game of Life)
**Test:** Conway's Game of Life simulation running for 500 generations on a 20x10 grid. This tests array access, modulo arithmetic, string manipulation (in `draw`), and logic branching.

### Code
```rust
WIDTH = 20;
HEIGHT = 10;
TOTAL_CELLS = WIDTH * HEIGHT;

func get_idx(x, y) => {
    wrapped_x, wrapped_y <~| (x + WIDTH) % WIDTH, (y + HEIGHT) % HEIGHT; 
    return wrapped_y * WIDTH + wrapped_x;
}

func count_neighbors(grid, x, y) => {
    count = 0;
    for dy in -1..1 -> for dx in -1..1 {
        if dx, dy <==| 0 -> continue;
        idx = get_idx(x + dx, y + dy);
        if grid[idx] == 1 -> count++;
    }
    return count;
}

func draw(grid, gen) => {
    buffer = f"\n--- Generation {gen} ---\n";
    for y in 0..HEIGHT-1 {
        line = "";
        for x in 0..WIDTH-1 {
            cell, char <~| grid[y * WIDTH + x], cell == 1 ?: "O ", ". ";
            line += char;
        }
        buffer = f"{buffer}{line}\n";
    }
}

func Main() => {
    grid = [];
    next_grid = [];
    
    TOTAL_CELLS times { 
        grid.push(0); 
        next_grid.push(0); 
    };

    # 80 random cells.
    80 times {
        grid[Random.between_exclusive(-1, TOTAL_CELLS)] = 1;
    } 

    generation = 0;
    
    500 times {
        draw(grid, generation);
        
        for y in 0..HEIGHT-1 -> for x in 0..WIDTH-1 {
            current_idx, is_alive, neighbors, new_state <~| y * WIDTH + x, grid[current_idx], count_neighbors(grid, x, y), match neighbors {
                3 -> 1;
                2 -> is_alive;
                rest -> 0;
            };
            next_grid[current_idx] = new_state;
        }

        grid >< next_grid; # Pointer swap.
        generation++;
    }
}
```

### Results
| Time | Mean (us) | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **307.29 ms** | 307,290.8 | 6,045.94 | 5,048.63 | 2000.0 | 14,948.78 KB |

---

## 4. Array Manipulation (Sieve of Eratosthenes)
**Test:** Finding all prime numbers up to 1,000,000. This tests dense array writes (`SetElement`) and nested loops.

### Code
```rust
use FluenceMath;

func primes(limit) => {
    maxSquareRoot = sqrt(limit);
    eliminated = [false] * (limit + 1);

    for i = 2; i <= maxSquareRoot; i += 1; {
        if !eliminated[i] {
            for j = i*i; j <= limit; j += i; {
                eliminated[j] = true;
            }
        }
    }

    output = [];
    for i = 2; i <= limit; i += 1; {
        if !eliminated[i] -> output.push(i);
    }
    
    return output;
}

func Main() => {
    n = 1_000_000;
    x = primes(n);
}
```

### Results
| Time | Mean (us) | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **259.61 ms** | 259,606.6 | 5,624.11 | 16,582.82 | 333.3 | 29,661.21 KB |

---

## 5. Branching Stress (Collatz Conjecture 3n + 1)
**Test:** Calculating the Collatz sequence for every number from 1 to 100,000. This tests conditional jump performance.

### Code
```rust
func Collatz() => {
    max_len, num_with_max_len, limit <2| 0 <| 100000;

    for n in 1..limit {
        len, term <~| 1, n;
        while term != 1 {
            if term % 2 == 0 -> term /= 2;
            else -> term = term * 3 + 1;
            len += 1;
        }
        if len > max_len -> max_len, num_with_max_len <~| len, n;
    } 
}

func Main() => Collatz();
```

### Results
| Time | Mean (us) | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **498.75 ms** | 498,748.6 | 3,221.45 | 2,690.05 | - | 54.66 KB |

---

## 6. String Algorithm (Levenshtein Distance)
**Test:** Calculating the edit distance between two large strings.
*   **Input 1:** ~3,500 characters.
*   **Input 2:** ~17,000 characters.

### Code
```rust
use FluenceMath;

func min(a, b, c) => (a < b ? (a < c ? a : c) : (b < c ? b : c));

func levenshtein(s1, s2) => {
    m, n, dp <~| s1.length(), s2.length(), [0..n];

    for i in 1..m {
        prev_row_prev_col, dp[0] <~| i - 1, i;

        for j in 1..n {
            temp, cost <~| dp[j], 0;
            if s1[i-1] != s2[j-1] -> cost = 1;
 
            dp[j] = min(dp[j] + 1, dp[j-1] + 1, prev_row_prev_col + cost);
            prev_row_prev_col = temp;
        }
    }
    return dp[n];
} 

func Main() => {
    # Inputs too large to include here.
    s1, s2 <~| "...", "..."; 
    dist = levenshtein(s1, s2);
}
```

### Results
| Time | Mean (us) | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **19.16 s** | 19,161,939.8 | 374,082.51 | 978,908.79 | - | 1.85 MB |

---

## 7. Compiler Performance
**Test:** Parsing and compiling a large source file containing complex constructs.
*   **Source Size:** ~44,000 characters (~1,750 lines) including 62 namespaces, 93 structs, 62 enums and 155 functions.

### Results
| Component | Time | Mean (us) | Allocated |
| :--- | :--- | :--- | :--- |
| **Lexer** | **0.32 ms** | 319.0 | 188.04 KB |
| **Parser + Lexer** | **1.32 ms** | 1,315.0 | 852.27 KB |
