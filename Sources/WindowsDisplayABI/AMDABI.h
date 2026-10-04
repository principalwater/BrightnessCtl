// SPDX-License-Identifier: MIT
// ABI declarations adapted from AMD ADL SDK headers (2016-2022 AMD).
// Full copyright and license: THIRD_PARTY_NOTICES.md. No SDK implementation.
#pragma once
typedef struct {
    int size, index;
    char udid[256];
    int bus, device, function, vendor;
    char name[256], display[256];
    int present, exists;
    char path[256], pathExt[256], pnp[256];
    int osIndex;
} BC_ADLAdapter;
typedef struct { int logicalDisplay, physicalDisplay, logicalAdapter, physicalAdapter; } BC_ADLDisplayID;
typedef struct {
    BC_ADLDisplayID id;
    int controller;
    char name[256], manufacturer[256];
    int type, output, connector, mask, value;
} BC_ADLDisplayInfo;
typedef void *(__stdcall *BC_ADLAllocate)(int);
typedef int (__cdecl *BC_ADLCreate)(BC_ADLAllocate, int);
typedef int (__cdecl *BC_ADLDestroy)(void);
typedef int (__cdecl *BC_ADLAdapterCount)(int *);
typedef int (__cdecl *BC_ADLAdapterInfo)(BC_ADLAdapter *, int);
typedef int (__cdecl *BC_ADLDisplays)(int, int *, BC_ADLDisplayInfo **, int);
typedef int (__cdecl *BC_ADLColorGet)(int, int, int, int *, int *, int *, int *, int *);
typedef int (__cdecl *BC_ADLColorSet)(int, int, int, int);
