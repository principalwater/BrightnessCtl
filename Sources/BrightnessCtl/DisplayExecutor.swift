// SPDX-License-Identifier: MIT

import Foundation
import Synchronization

/// A dedicated OS thread keeps blocking display APIs off Swift's shared pool.
/// The queue is mutex-protected; jobs execute only on this executor's thread.
final class DisplayExecutor: SerialExecutor, @unchecked Sendable {
    private struct State {
        var jobs: [UnownedJob] = []
        var stopping = false
    }
    private let state = Mutex(State())
    private let condition = NSCondition()
    private var thread: Thread?

    init() {
        let thread = Thread { [weak self] in self?.run() }
        thread.name = "BrightnessCtl display APIs"
        self.thread = thread
        thread.start()
    }

    func enqueue(_ job: consuming ExecutorJob) {
        let job = UnownedJob(job)
        condition.lock()
        state.withLock { $0.jobs.append(job) }
        condition.signal()
        condition.unlock()
    }

    private func run() {
        while true {
            condition.lock()
            while state.withLock({ $0.jobs.isEmpty && !$0.stopping }) { condition.wait() }
            let job: UnownedJob? = state.withLock {
                if !$0.jobs.isEmpty { return $0.jobs.removeFirst() }
                return nil
            }
            condition.unlock()
            guard let job else { return }
            job.runSynchronously(on: asUnownedSerialExecutor())
        }
    }

    /// Call only after the controller has restored its output and accepted no jobs.
    func stop() {
        condition.lock()
        state.withLock { $0.stopping = true }
        condition.signal()
        condition.unlock()
    }
}
