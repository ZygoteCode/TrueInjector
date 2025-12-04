# TrueInjector

Advanced DLL injection tool with multiple injection techniques for Windows processes.

![TrueInjector Interface](syringe.ico)

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Injection Methods](#injection-methods)
  - [Standard Injection](#standard-injection)
  - [Manual Mapping](#manual-mapping)
  - [Thread Hijacking](#thread-hijacking)
- [Installation](#installation)
- [Usage](#usage)
- [Technical Details](#technical-details)
- [Requirements](#requirements)
- [License](#license)

## Overview

TrueInjector is a powerful DLL injection tool designed for security researchers, reverse engineers, and developers who need to inject DLLs into running processes on Windows systems. It offers multiple injection techniques to bypass various anti-cheat and security mechanisms.

The tool provides a clean, intuitive graphical interface built with Guna UI2 and MetroSuite, allowing users to easily select target processes, choose injection methods, and customize injection parameters.

## Features

- **Multiple Injection Techniques**: Supports Standard Injection, Manual Mapping, and Thread Hijacking
- **Process Enumeration**: Lists all running processes with their PID and names
- **Process Filtering**: Search for specific processes by name or PID
- **Architecture Validation**: Automatically checks DLL and process architecture compatibility
- **Customizable Injection Parameters**: Fine-tune injection with various API combinations
- **Real-time Process Monitoring**: Continuously updates the process list
- **Modern UI**: Clean, dark-themed interface with smooth animations

## Injection Methods

### Standard Injection

The traditional DLL injection method using Windows API functions. This technique allocates memory in the target process, writes the DLL path to that memory, and creates a remote thread to execute LoadLibrary.

#### Configuration Options:

- **Load Library Function**:
  - `LoadLibraryA`: ANSI version of LoadLibrary
  - `LoadLibraryW`: Unicode version of LoadLibrary

- **Thread Creation Method**:
  - `CreateRemoteThread`: Standard Windows API
  - `RtlCreateUserThread`: Native NT API
  - `NtCreateThreadEx`: Low-level NT API
  - `NtQueueApcThread`: Asynchronous procedure call injection
  - `NtQueueApcThreadEx`: Extended APC injection

- **Memory Writing Method**:
  - `WriteProcessMemory`: Standard Windows API
  - `NtWriteVirtualMemory`: Native NT API
  - `ZwWriteVirtualMemory`: Wrapper for NtWriteVirtualMemory
  - `NtCreateSection + NtMapViewOfSection`: Section object mapping
  - `CreateFileMapping + MapViewOfFile + NtMapViewOfSection`: File mapping approach

- **Memory Allocation Method**:
  - `VirtualAllocEx`: Standard memory allocation
  - `NtAllocateVirtualMemory`: Native NT API allocation
  - `NtCreateSection`: Section-based allocation
  - `CreateFileMapping`: File mapping allocation
  - `VirtualAlloc2`: Extended allocation API

- **Process Handle Management**:
  - `OpenProcess + CloseHandle`: Standard Windows API
  - `NtOpenProcess + NtClose`: Native NT API

### Manual Mapping

Also known as "Reflective DLL Injection," this advanced technique manually loads a DLL into a target process without using LoadLibrary. It parses the PE headers, resolves imports, handles relocations, and executes the DLL entry point directly.

#### Key Components:

- **ManualMapper.dll**: C++ library implementing the manual mapping algorithm
- **PE Parsing**: Reads and interprets Portable Executable headers
- **Import Resolution**: Resolves DLL dependencies using LoadLibrary and GetProcAddress
- **Relocation Handling**: Adjusts addresses based on the actual load location
- **TLS Callback Support**: Handles Thread Local Storage callbacks
- **SEH Support**: Structured Exception Handling support for x64
- **Shellcode Execution**: Uses custom shellcode to initialize the injected DLL

### Thread Hijacking

This technique hijacks an existing thread in the target process and redirects its execution to load the DLL. It suspends a thread, modifies its context to point to our shellcode, and resumes it.

#### Key Components:

- **ThreadHijacker.dll**: C++ library implementing the thread hijacking algorithm
- **Thread Enumeration**: Finds threads in the target process
- **Context Manipulation**: Modifies thread register state
- **Shellcode Generation**: Creates assembly code to load the DLL
- **Stealth Injection**: Less detectable than creating new threads

## Installation

1. Clone or download the repository
2. Ensure you have the required dependencies installed:
   - .NET Framework 4.8.1
   - Visual Studio with C# and C++ support
3. Restore NuGet packages:
   ```
   nuget restore TrueInjector.sln
   ```
4. Build the solution:
   ```
   msbuild TrueInjector.sln /p:Configuration=Release
   ```

## Usage

1. Launch TrueInjector.exe
2. Select a target process from the process list:
   - Use the search box to filter processes by name or PID
   - Click "Refresh processes" to update the list
3. Load a DLL file:
   - Click "Load DLL file..." and select your DLL
   - Or enter the full path in the DLL file path textbox
4. Choose an injection method:
   - **Standard Injection**: Traditional LoadLibrary injection
   - **Manual Mapping**: Advanced reflective injection
   - **Thread Hijacking**: Thread redirection technique
5. Configure injection parameters (for Standard Injection):
   - Select appropriate APIs for your target process
6. Click "Inject DLL file into selected process"
7. Check for success or error messages

## Technical Details

### Architecture

TrueInjector consists of three main components:

1. **Main Application** (C#):
   - Graphical user interface
   - Process enumeration and selection
   - Injection parameter configuration
   - Integration with native DLLs

2. **ManualMapper.dll** (C++):
   - Implements manual mapping injection
   - PE parsing and manipulation
   - Memory management using NT APIs

3. **ThreadHijacker.dll** (C++):
   - Implements thread hijacking injection
   - Thread manipulation and context switching
   - Assembly shellcode generation

### Security Considerations

- TrueInjector is intended for legitimate purposes such as:
  - Software testing and debugging
  - Security research
  - Educational purposes
  - Reverse engineering
- Use responsibly and only on software you own or have explicit permission to analyze
- Some antivirus software may flag injection tools as suspicious - this is normal

### Dependencies

- **Guna.UI2**: Modern Windows Forms controls
- **MetroSuite**: Custom form styling library
- **PeNet**: PE file analysis library
- **Costura.Fody**: Embedding assemblies into executables
- **Native Windows APIs**: kernel32.dll, ntdll.dll

## Requirements

- **Operating System**: Windows 7 or higher (x64 recommended)
- **Runtime**: .NET Framework 4.8.1
- **Architecture**: x64 (compatible with both x86 and x64 target processes)
- **Privileges**: Administrator rights recommended for injecting into system processes

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

*Note: This tool is for educational and legitimate security research purposes only. The developers are not responsible for any misuse or damage caused by this tool.*
