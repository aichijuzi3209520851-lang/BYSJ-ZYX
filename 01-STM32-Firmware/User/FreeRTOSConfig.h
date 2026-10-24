/**
  ******************************************************************************
  * @file    User/FreeRTOSConfig.h
  * @brief   FreeRTOS V10.5.1 内核配置(STM32F103C8T6 @72MHz, 20KB RAM)
  * @author  LCW
  * @version V1.1
  * @date    2026-10-25
  * @note    中断优先级约定(4位优先级, 数值越小优先级越高):
  *            0~4  : 永远不被内核屏蔽, 禁止调用FromISR API
  *            5    : configMAX_SYSCALL_INTERRUPT_PRIORITY阈值
  *            6    : USART1/USART3(可安全调用FromISR)
  *            15   : 内核固定(PendSV/SysTick)
  ******************************************************************************
  */
#ifndef FREERTOS_CONFIG_H
#define FREERTOS_CONFIG_H

/* ==================== 基础调度 ==================== */
#define configUSE_PREEMPTION                    1      /* 抢占式调度 */
#define configUSE_TIME_SLICING                  1      /* 同优先级时间片轮转 */
#define configUSE_PORT_OPTIMISED_TASK_SELECTION 1      /* CM3 CLZ加速选任务 */
#define configUSE_TICKLESS_IDLE                 0
#define configCPU_CLOCK_HZ                      (72000000UL)
#define configTICK_RATE_HZ                      ((TickType_t)1000)   /* 1ms心跳 */
#define configMAX_PRIORITIES                    7      /* 0最低(idle)~6 */
#define configMINIMAL_STACK_SIZE                ((uint16_t)128)
#define configMAX_TASK_NAME_LEN                 8
#define configIDLE_SHOULD_YIELD                 1
#define configUSE_16_BIT_TICKS                  0      /* 32位tick */
#define configUSE_TASK_NOTIFICATIONS            1
#define configUSE_CO_ROUTINES                   0

/* ==================== 内存 ==================== */
#define configTOTAL_HEAP_SIZE                   ((size_t)(10 * 1024))  /* heap_4堆 */
#define configSUPPORT_DYNAMIC_ALLOCATION        1
#define configSUPPORT_STATIC_ALLOCATION         0
#define configAPPLICATION_ALLOCATED_HEAP        0

/* ==================== 同步原语 ==================== */
#define configUSE_MUTEXES                       1
#define configUSE_RECURSIVE_MUTEXES             0
#define configUSE_COUNTING_SEMAPHORES           0
#define configQUEUE_REGISTRY_SIZE               0
#define configUSE_TIMERS                        0      /* 未用软件定时器(周期用vTaskDelay) */
#define configTIMER_TASK_PRIORITY               0
#define configTIMER_QUEUE_LENGTH                0
#define configTIMER_TASK_STACK_DEPTH            0

/* ==================== 中断优先级(与Keil工程NVIC配置配套) ==================== */
#define configPRIO_BITS                         4                              /* STM32F1实现4位优先级 */
#define configLIBRARY_LOWEST_INTERRUPT_PRIORITY 15                             /* 最低: 内核用 */
#define configLIBRARY_MAX_SYSCALL_INTERRUPT_PRIORITY 5                         /* 可屏蔽阈值 */
#define configKERNEL_INTERRUPT_PRIORITY         (configLIBRARY_LOWEST_INTERRUPT_PRIORITY << (8 - configPRIO_BITS))      /* 0xF0 */
#define configMAX_SYSCALL_INTERRUPT_PRIORITY    (configLIBRARY_MAX_SYSCALL_INTERRUPT_PRIORITY << (8 - configPRIO_BITS)) /* 0x50 */

/* ==================== 钩子与诊断 ==================== */
#define configCHECK_FOR_STACK_OVERFLOW          2      /* 方式2: 指针越过+末尾魔数 */
#define configUSE_MALLOC_FAILED_HOOK            0
#define configUSE_IDLE_HOOK                     0
#define configUSE_TICK_HOOK                     0

/* ==================== 调试裁剪(省Flash) ==================== */
#define configUSE_TRACE_FACILITY                0
#define configUSE_STATS_FORMATTING_FUNCTIONS    0
#define configGENERATE_RUN_TIME_STATS           0
#define configUSE_POSIX_ERRNO                   0

/* ==================== 内核API裁剪(INCLUDE_*) ==================== */
#define INCLUDE_vTaskPrioritySet                1
#define INCLUDE_uxTaskPriorityGet               1
#define INCLUDE_vTaskDelete                     1
#define INCLUDE_vTaskSuspend                    1
#define INCLUDE_vTaskDelayUntil                 1
#define INCLUDE_vTaskDelay                      1
#define INCLUDE_xTaskGetSchedulerState          1
#define INCLUDE_xTaskGetCurrentTaskHandle       1
#define INCLUDE_xTaskGetIdleTaskHandle          1
#define INCLUDE_eTaskGetState                   1
#define INCLUDE_xTaskAbortDelay                 1
#define INCLUDE_xTaskGetHandle                  1
#define INCLUDE_xTaskResumeFromISR              1
#define INCLUDE_xTimerPendFunctionCall          0
#define INCLUDE_xQueueGetMutexHolder            1

/* ==================== 断言 ==================== */
#define configASSERT(x)                                                         \
    do {                                                                        \
        if ((x) == 0) { taskDISABLE_INTERRUPTS(); for (;;); }                   \
    } while (0)

/* ==================== 内核异常入口映射 ====================
 * stm32f10x_it.c 中同名空函数已删除, 由以下映射接管向量表 */
#define vPortSVCHandler                         SVC_Handler
#define xPortPendSVHandler                      PendSV_Handler
#define xPortSysTickHandler                     SysTick_Handler

#endif /* FREERTOS_CONFIG_H */
